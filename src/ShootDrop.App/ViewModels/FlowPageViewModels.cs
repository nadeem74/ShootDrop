using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;

namespace ShootDrop.App.ViewModels;

public sealed record PlanRow(string Card, string Folder, string Files, string Size);
public sealed record TreeLine(string Text, double Indent, string Note, bool Empty);

/// <summary>Review before copying (screen 3).</summary>
public sealed partial class ReviewPageViewModel : PageViewModel
{
    private readonly ShootDropContext _ctx;
    private readonly CopyPreview _preview;

    public ReviewPageViewModel(ShootDropContext ctx, CopyPreview preview)
    {
        _ctx = ctx;
        _preview = preview;
        var plan = preview.Plan;
        var name = Path.GetFileName(plan.ProjectRoot);
        Title = $"Copy {Format.Files(plan.Items.Count)} ({Format.Bytes(plan.TotalBytes)}) into “{name}”";
        Subtitle = $"{preview.Request.Date.Describe(ctx.Today)} · {Format.Kinds(preview.Request.Kinds)} · nothing has been copied yet";

        foreach (var g in plan.Items.GroupBy(i => (i.CardName, i.Folder)))
            Rows.Add(new PlanRow(g.Key.CardName, Format.Folder(g.Key.Folder), g.Count().ToString(), Format.Bytes(g.Sum(i => i.Source.Size))));
        TotalFiles = plan.Items.Count.ToString();
        TotalSize = Format.Bytes(plan.TotalBytes);

        var free = ctx.Platform.FreeBytes(ctx.Settings.ProjectsRoot);
        EnoughSpace = free is null || free.Value > plan.TotalBytes;
        Checks.Add(free is null ? "Couldn't read free space on the projects drive"
            : EnoughSpace ? $"Enough space: {Format.Bytes(free.Value)} free, this needs {Format.Bytes(plan.TotalBytes)}"
            : $"Not enough space: {Format.Bytes(free.Value)} free, this needs {Format.Bytes(plan.TotalBytes)}");
        Checks.Add(Directory.Exists(plan.ProjectRoot)
            ? "Project folder already exists. New files are added; nothing in it is overwritten."
            : "New project folder, made from your folder structure");
        Checks.Add("Cards are opened read-only. Nothing on them is changed or deleted.");
        Checks.Add("Each file is checked with a checksum after it is copied");

        Tree.Add(new TreeLine(name, 0, "", false));
        var counts = plan.Items.GroupBy(i => i.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var folders = ctx.Settings.Template.Folders.Concat(counts.Keys)
            .SelectMany(f => Prefixes(f)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        foreach (var f in folders)
        {
            var depth = f.Count(ch => ch == '/') + 1;
            var n = counts.Where(kv => kv.Key.Equals(f, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
            var isLeaf = !folders.Any(o => o.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
            Tree.Add(new TreeLine(f.Split('/')[^1], depth * 18, n > 0 ? $"+{n}" : isLeaf ? "empty" : "", n == 0 && isLeaf));
        }
        Location = ctx.Settings.ProjectsRoot.Replace("/", " › ").Trim(' ', '›') + " › " + name;
    }

    private static IEnumerable<string> Prefixes(string folder)
    {
        var parts = folder.Trim('/').Split('/');
        for (var i = 1; i <= parts.Length; i++) yield return string.Join('/', parts[..i]);
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string TotalFiles { get; }
    public string TotalSize { get; }
    public string Location { get; }
    public bool EnoughSpace { get; }
    public ObservableCollection<PlanRow> Rows { get; } = [];
    public ObservableCollection<string> Checks { get; } = [];
    public ObservableCollection<TreeLine> Tree { get; } = [];

    public bool OpenFinderWhenDone
    {
        get => _ctx.Settings.OpenFinderWhenDone;
        set { _ctx.Settings.OpenFinderWhenDone = value; _ctx.Save(); OnPropertyChanged(); }
    }

    public bool EjectWhenDone
    {
        get => _ctx.Settings.EjectWhenDone;
        set { _ctx.Settings.EjectWhenDone = value; _ctx.Save(); OnPropertyChanged(); }
    }

    [RelayCommand] private void Back() => _ctx.Shell.GoMain();

    [RelayCommand]
    private void StartCopy()
    {
        if (EnoughSpace) _ctx.Shell.GoCopying(_preview);
    }
}

public sealed partial class CardProgress : ObservableObject
{
    public CardProgress(string name, string folder, long bytes, int files)
    {
        Name = name;
        Folder = folder;
        Bytes = bytes;
        Files = files;
        _status = "Waiting";
    }

    public string Name { get; }
    public string Folder { get; }
    public long Bytes { get; }
    public int Files { get; }
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _status;
    [ObservableProperty] private bool _isDone;
}

/// <summary>Copying and verifying (screen 4).</summary>
public sealed partial class CopyingPageViewModel : PageViewModel
{
    private readonly ShootDropContext _ctx;
    private readonly CopyPreview _preview;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = new();
    private readonly long[] _starts;

    public CopyingPageViewModel(ShootDropContext ctx, CopyPreview preview)
    {
        _ctx = ctx;
        _preview = preview;
        var items = preview.Plan.Items;
        _starts = new long[items.Count];
        long acc = 0;
        for (var i = 0; i < items.Count; i++) { _starts[i] = acc; acc += items[i].Source.Size; }
        foreach (var g in items.GroupBy(i => i.CardName))
            Cards.Add(new CardProgress(g.Key, string.Join(", ", g.Select(i => Format.Folder(i.Folder)).Distinct()), g.Sum(i => i.Source.Size), g.Count()));
        Title = $"Copying “{Path.GetFileName(preview.Plan.ProjectRoot)}”";
    }

    public string Title { get; }
    public ObservableCollection<CardProgress> Cards { get; } = [];
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _percentText = "0%";
    [ObservableProperty] private string _headline = "Starting…";
    [ObservableProperty] private string _eta = "";
    [ObservableProperty] private string _current = "";
    [ObservableProperty] private bool _cancelling;

    public override void OnShown() => _ = RunAsync();

    private async Task RunAsync()
    {
        _clock.Start();
        var plan = _preview.Plan;
        var progress = new Progress<CopyProgress>(Update);
        try
        {
            var result = await _ctx.Service.RunAsync(_preview, _ctx.Settings, progress, _cts.Token);
            _clock.Stop();
            _ctx.Save(); // remembers "last used"
            if (_ctx.Settings.OpenFinderWhenDone) _ctx.Platform.OpenFolder(plan.ProjectRoot);
            var ejected = false;
            if (_ctx.Settings.EjectWhenDone && result.Failed == 0) ejected = await EjectAll(_ctx, _preview.Cards.Select(c => c.Card));
            _ctx.Shell.GoDone(new DonePageViewModel(_ctx, _preview, result, _clock.Elapsed, ejected));
        }
        catch (OperationCanceledException)
        {
            _ctx.Shell.GoDone(DonePageViewModel.Cancelled(_ctx, _preview));
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _ctx.Shell.GoDone(DonePageViewModel.Error(_ctx, _preview, e.Message));
        }
    }

    internal static async Task<bool> EjectAll(ShootDropContext ctx, IEnumerable<DetectedCard> cards)
    {
        var ok = true;
        foreach (var c in cards) ok &= await ctx.Platform.EjectAsync(c.Volume.Path);
        ctx.Monitor.Refresh();
        return ok;
    }

    private void Update(CopyProgress p)
    {
        Percent = p.Fraction * 100;
        PercentText = $"{Math.Floor(p.Fraction * 100)}%";
        Headline = $"{Math.Max(0, p.FileNumber - 1)} of {p.FileCount} files · {Format.Bytes(p.BytesDone)} of {Format.Bytes(p.BytesTotal)}";
        var secs = _clock.Elapsed.TotalSeconds;
        if (secs > 2 && p.BytesDone > 0)
        {
            var speed = p.BytesDone / secs;
            Eta = $"About {Format.Duration(TimeSpan.FromSeconds((p.BytesTotal - p.BytesDone) / speed))} left · {Format.Bytes((long)speed)}/s";
        }
        var item = _preview.Plan.Items[Math.Clamp(p.FileNumber - 1, 0, _preview.Plan.Items.Count - 1)];
        Current = $"{item.Source.Name}  →  {Format.Folder(item.Folder)}";

        foreach (var card in Cards)
        {
            long done = 0;
            var items = _preview.Plan.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].CardName != card.Name) continue;
                done += Math.Clamp(p.BytesDone - _starts[i], 0, items[i].Source.Size);
            }
            var f = card.Bytes == 0 ? 1 : (double)done / card.Bytes;
            card.Percent = f * 100;
            card.IsDone = f >= 1;
            card.Status = f >= 1 ? $"{card.Files} of {card.Files} copied" : f > 0 ? $"{Math.Floor(f * 100)}%" : "Waiting";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelling = true;
        _cts.Cancel();
    }
}

public sealed record ResultRow(string Card, string Folder, string Files, string Result, bool Ok);

/// <summary>Done (screen 5).</summary>
public sealed partial class DonePageViewModel : PageViewModel
{
    private readonly ShootDropContext _ctx;
    private readonly CopyPreview _preview;

    public DonePageViewModel(ShootDropContext ctx, CopyPreview preview, CopyRunResult result, TimeSpan elapsed, bool ejected)
    {
        _ctx = ctx;
        _preview = preview;
        var total = result.Files.Count;
        var name = Path.GetFileName(preview.Plan.ProjectRoot);
        Success = result.Failed == 0;
        Headline = Success
            ? $"{result.Copied + result.Skipped} of {total} files copied and verified"
            : $"{result.Failed} of {total} files failed";
        Subtitle = $"{Format.Bytes(preview.Plan.TotalBytes)} into “{name}” in {Format.Duration(elapsed)}. Your cards were not changed."
                   + (result.Skipped > 0 ? $" {result.Skipped} were already there and were skipped." : "");
        foreach (var g in result.Files.GroupBy(f => f.Item.CardName))
        {
            var failed = g.Count(f => f.Outcome == CopyOutcome.Failed);
            Rows.Add(new ResultRow(g.Key, string.Join(", ", g.Select(f => Format.Folder(f.Item.Folder)).Distinct()), g.Count().ToString(),
                failed == 0 ? "All match" : $"{failed} failed", failed == 0));
        }
        foreach (var f in result.Files.Where(f => f.Outcome == CopyOutcome.Failed))
            Failures.Add($"{f.Item.Source.Name}: {f.Error}");
        ReportText = $"Report saved as {name}/{CopyReport.FileName} with every file name, size and checksum.";
        var airdrop = ctx.Settings.Template.Folders.FirstOrDefault(f => f.EndsWith("iPhone", StringComparison.OrdinalIgnoreCase));
        AirDropNote = airdrop is null ? "" : $"{Format.Folder(airdrop)} is ready for your AirDrop.";
        _ejected = ejected;
        EjectText = ejected ? "Cards ejected. You can pull them out." : "";
    }

    private DonePageViewModel(ShootDropContext ctx, CopyPreview preview, string headline, string subtitle)
    {
        _ctx = ctx;
        _preview = preview;
        Success = false;
        Headline = headline;
        Subtitle = subtitle;
        ReportText = "";
        AirDropNote = "";
    }

    public static DonePageViewModel Cancelled(ShootDropContext ctx, CopyPreview p) =>
        new(ctx, p, "Copy cancelled", "Finished files are kept in the project. Run the same copy again to copy the rest; finished files are skipped.");

    public static DonePageViewModel Error(ShootDropContext ctx, CopyPreview p, string message) =>
        new(ctx, p, "Copy stopped", message);

    public bool Success { get; }
    public bool NotSuccess => !Success;
    public string Headline { get; }
    public string Subtitle { get; }
    public string ReportText { get; }
    public string AirDropNote { get; }
    public bool HasAirDropNote => AirDropNote.Length > 0;
    public ObservableCollection<ResultRow> Rows { get; } = [];
    public ObservableCollection<string> Failures { get; } = [];
    public bool HasFailures => Failures.Count > 0;

    [ObservableProperty] private bool _ejected;
    [ObservableProperty] private string _ejectText = "";

    public bool CanEject => !Ejected && OperatingSystem.IsMacOS();

    partial void OnEjectedChanged(bool value) => OnPropertyChanged(nameof(CanEject));

    [RelayCommand] private void OpenInFinder() => _ctx.Platform.OpenFolder(_preview.Plan.ProjectRoot);

    [RelayCommand]
    private async Task EjectAsync()
    {
        var ok = await CopyingPageViewModel.EjectAll(_ctx, _preview.Cards.Select(c => c.Card));
        Ejected = ok;
        EjectText = ok ? "Cards ejected. You can pull them out." : "Couldn't eject every card. Is a file on it still open?";
    }

    [RelayCommand] private void NewCopy() => _ctx.Shell.GoReady();
}
