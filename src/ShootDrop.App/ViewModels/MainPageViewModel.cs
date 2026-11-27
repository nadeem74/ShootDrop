using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;
using ShootDrop.Core.SmartBox;

namespace ShootDrop.App.ViewModels;

public sealed partial class SetupChip : ObservableObject
{
    public SetupChip(string name, Action<SetupChip> use)
    {
        Name = name;
        UseCommand = new RelayCommand(() => use(this));
    }

    public string Name { get; }
    public IRelayCommand UseCommand { get; }
    [ObservableProperty] private bool _isActive;
}

public sealed class TryChip(string text, Action<string> run)
{
    public string Text { get; } = text;
    public IRelayCommand RunCommand { get; } = new RelayCommand(() => run(text));
}

/// <summary>One card in the main window: tick to include; unknown cards choose a folder (FR-07).</summary>
public sealed partial class CardItemViewModel : ObservableObject
{
    private readonly MainPageViewModel _owner;

    public CardItemViewModel(MainPageViewModel owner, DetectedCard card, IEnumerable<string> folders, string? routedFolder)
    {
        _owner = owner;
        Card = card;
        Folders = new ObservableCollection<string>(folders);
        _destination = routedFolder;
        _isSelected = true;
    }

    public DetectedCard Card { get; }
    public string Name => Card.Name;
    public bool IsMic => Card.Profile?.Id == "dji-mic";
    public bool IsUnknown => Card.Profile is null;
    public string Device => Card.Profile is { } p ? $"{p.Name} → {Format.Folder(_owner.FolderFor(Card))}"
        : Destination is { Length: > 0 } d ? $"Unknown device → {Format.Folder(d)}" : "Unknown device · choose where it goes";
    public ObservableCollection<string> Folders { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _destination;
    [ObservableProperty] private string _stat = "…";
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private bool _needsFolder;

    public bool ShowFolderPicker => IsUnknown && IsSelected && HasFiles;

    /// <summary>Blue border: this card will be copied.</summary>
    public bool Highlight => IsSelected && HasFiles;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFolderPicker));
        OnPropertyChanged(nameof(Highlight));
        _owner.Recompute();
    }

    partial void OnHasFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFolderPicker));
        OnPropertyChanged(nameof(Highlight));
    }

    partial void OnDestinationChanged(string? value)
    {
        _owner.RememberCardFolder(Card, value);
        OnPropertyChanged(nameof(Device));
    }
}

public enum DateMode
{
    Today,
    Yesterday,
    Last3,
    Custom,
}

/// <summary>The full window (FR-03..FR-07, FR-13, FR-19, FR-20).</summary>
public sealed partial class MainPageViewModel : PageViewModel
{
    private readonly ShootDropContext _ctx;
    private int _version;
    private bool _loading;

    public MainPageViewModel(ShootDropContext ctx)
    {
        _ctx = ctx;
        _loading = true;
        var setup = ctx.Settings.CurrentSetup;
        _smartOn = ctx.Settings.SmartBoxOn;
        _projectName = ctx.DefaultProjectName;
        _quickProjectName = ctx.DefaultProjectName;
        ApplySetupFields(setup);
        foreach (var t in new[] { "all cards, yesterday for Sinhagad trip", "Pocket only, last 3 days", "audio only today", "kal ka sab footage with photos", "yesterday till 1 pm into A, till 6 pm into B" })
            Tries.Add(new TryChip(t, RunSmart));
        LoadSetupChips();
        _loading = false;
    }

    public ObservableCollection<CardItemViewModel> Cards { get; } = [];
    public ObservableCollection<SetupChip> Setups { get; } = [];
    public ObservableCollection<TryChip> Tries { get; } = [];
    public ObservableCollection<string> QuickPills { get; } = [];
    public string[] DateOptions { get; } = ["Today", "Yesterday", "Last 3 days", "Pick dates"];
    /// <summary>The footer message is a problem that stops the copy: shown in red.</summary>
    [ObservableProperty] private bool _warnIsProblem;

    [ObservableProperty] private bool _noCards;

    // ---- Quick copy (Copy like last time) -------------------------------------------------
    [ObservableProperty] private string _quickButton = "Copy now";
    [ObservableProperty] private string _quickProjectName = "";
    [ObservableProperty] private bool _quickCanCopy;

    // ---- Smart box -------------------------------------------------------------------------
    [ObservableProperty] private bool _smartOn;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _understood = "";
    [ObservableProperty] private bool _showUnderstood;
    [ObservableProperty] private bool _showMiss;

    // ---- Dates -----------------------------------------------------------------------------
    [ObservableProperty] private int _dateIndex;
    [ObservableProperty] private DateTime? _customFrom;
    [ObservableProperty] private DateTime? _customTo;
    [ObservableProperty] private string _dateLabel = "";
    public bool IsCustom => DateIndex == (int)DateMode.Custom;

    // ---- File types ------------------------------------------------------------------------
    [ObservableProperty] private bool _video;
    [ObservableProperty] private bool _audio;
    [ObservableProperty] private bool _photos;
    [ObservableProperty] private bool _proxies;

    // ---- Project & footer ------------------------------------------------------------------
    [ObservableProperty] private string _projectName;
    [ObservableProperty] private string _newSetupName = "";
    [ObservableProperty] private string _totalLine = "";
    [ObservableProperty] private string _warnLine = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    private bool _canReview;

    public string ProjectsRootText => Path.GetFileName(_ctx.Settings.ProjectsRoot.TrimEnd('/')) is { Length: > 0 } n
        ? $"{Path.GetFileName(Path.GetDirectoryName(_ctx.Settings.ProjectsRoot.TrimEnd('/')) ?? "")} › {n}" : "(not set)";
    public string TemplateText => _ctx.Settings.Template.Name;

    public override void OnShown()
    {
        _ctx.Monitor.Changed += OnCardsChanged;
        RebuildCards();
    }

    public override void OnHidden() => _ctx.Monitor.Changed -= OnCardsChanged;

    private void OnCardsChanged(object? sender, EventArgs e) => RebuildCards();

    internal string FolderFor(DetectedCard card)
    {
        var r = _ctx.Settings.Routing.Resolve(card, FileKind.Video);
        return r.Status == Core.Projects.RouteStatus.Copy ? r.Folder : "?";
    }

    private void RebuildCards()
    {
        var folders = _ctx.Settings.Template.Folders.ToList();
        var previous = Cards.ToDictionary(c => c.Card.CardId, c => c.IsSelected);
        Cards.Clear();
        foreach (var card in _ctx.Monitor.Cards)
        {
            _ctx.Settings.Routing.ByCard.TryGetValue(card.CardId, out var routed);
            var item = new CardItemViewModel(this, card, folders, routed);
            if (previous.TryGetValue(card.CardId, out var sel)) item.IsSelected = sel;
            Cards.Add(item);
        }
        NoCards = Cards.Count == 0;
        Recompute();
    }

    internal void RememberCardFolder(DetectedCard card, string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        _ctx.Settings.Routing.ByCard[card.CardId] = folder;
        _ctx.Save();
        Recompute();
    }

    // ---- Building the request --------------------------------------------------------------

    private DateRule CurrentDate() => (DateMode)DateIndex switch
    {
        DateMode.Yesterday => DateRule.Yesterday,
        DateMode.Last3 => DateRule.LastDays(3),
        DateMode.Custom => DateRule.Between(
            DateOnly.FromDateTime(CustomFrom ?? _ctx.Today.ToDateTime(TimeOnly.MinValue)),
            DateOnly.FromDateTime(CustomTo ?? CustomFrom ?? _ctx.Today.ToDateTime(TimeOnly.MinValue))),
        _ => DateRule.Today,
    };

    private HashSet<FileKind> CurrentKinds()
    {
        var k = new HashSet<FileKind>();
        if (Video) k.Add(FileKind.Video);
        if (Audio) k.Add(FileKind.Audio);
        if (Photos) k.Add(FileKind.Photo);
        if (Proxies) k.Add(FileKind.Proxy);
        return k;
    }

    private IReadOnlyList<string>? SelectedKeys() =>
        Cards.Count > 0 && Cards.All(c => c.IsSelected) ? null : Cards.Where(c => c.IsSelected).Select(c => c.Card.CardId).ToList();

    private string Name() => string.IsNullOrWhiteSpace(ProjectName) ? _ctx.DefaultProjectName : ProjectName.Trim();

    private string QuickName() => string.IsNullOrWhiteSpace(QuickProjectName) ? _ctx.DefaultProjectName : QuickProjectName.Trim();

    private CopyRequest CurrentRequest(IReadOnlyList<string>? keys) => new(Name(), CurrentDate(), CurrentKinds(), keys, _splits, ExcludedFiles);

    // ---- Split by time ---------------------------------------------------------------------
    // "till 1 pm into A, till 6 pm into B" from the smart box, or "Split by breaks".

    private IReadOnlyList<ProjectSplit>? _splits;
    public ObservableCollection<string> SplitLines { get; } = [];
    [ObservableProperty] private bool _hasSplits;
    [ObservableProperty] private string _splitNote = "";

    private void SetSplits(IReadOnlyList<ProjectSplit>? splits)
    {
        _splits = splits is { Count: > 0 } ? splits : null;
        SplitLines.Clear();
        if (_splits is not null)
            foreach (var sp in _splits) SplitLines.Add($"{sp.ProjectName ?? Name()}  ·  {sp.Window.Describe()}");
        HasSplits = _splits is not null;
    }

    [RelayCommand]
    private void ClearSplit()
    {
        SetSplits(null);
        SplitNote = "";
        Recompute();
    }

    /// <summary>"Split by breaks in recording": turns on the timeline, which groups clips where recording paused.</summary>
    [RelayCommand]
    private void SplitByBreaks() => TimelineOn = true;

    /// <summary>Re-scans with the current choices and updates every card's numbers and the footer.</summary>
    internal async void Recompute()
    {
        if (_loading) return;
        var version = ++_version;
        DateLabel = CurrentDate().Describe(_ctx.Today);
        OnPropertyChanged(nameof(IsCustom));

        CopyPreview all;
        try
        {
            all = await _ctx.PreviewAsync(CurrentRequest(null)); // all cards, for per-card numbers
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException)
        {
            TotalLine = "Can't scan yet";
            WarnLine = e.Message;
            WarnIsProblem = true;
            CanReview = false;
            return;
        }
        if (version != _version) return;

        // The timeline follows the selected cards' clips; if it changed while it's in use, plan again with it.
        if (UpdateTimeline(all.Cards.Where(cf => Cards.Any(c => c.IsSelected && c.Name == cf.Card.Name)).ToList()))
        {
            Recompute();
            return;
        }

        long bytes = 0;
        int files = 0, cards = 0;
        var blocked = false;
        foreach (var c in Cards)
        {
            var items = all.Plan.Items.Where(i => i.CardName == c.Name).ToList();
            var unrouted = all.Plan.Unrouted.Contains(c.Card);
            c.HasFiles = items.Count > 0 || unrouted;
            c.NeedsFolder = unrouted;
            c.Stat = items.Count > 0 ? $"{Format.Files(items.Count)} · {Format.Bytes(items.Sum(i => i.Source.Size))}"
                : unrouted ? "Has files · choose a folder" : "Nothing on these dates";
            if (!c.IsSelected) continue;
            if (unrouted) blocked = true;
            if (items.Count == 0) continue;
            cards++;
            files += items.Count;
            bytes += items.Sum(i => i.Source.Size);
        }

        var projects = all.Plan.IsSplit ? all.Plan.AllParts.Count(p => p.Items.Count > 0) : 1;
        TotalLine = files > 0 ? $"{cards} {(cards == 1 ? "card" : "cards")} · {Format.Files(files)} · {Format.Bytes(bytes)}"
                                + (projects > 1 ? $" · {projects} projects" : "")
            : "Nothing to copy with these choices";
        var outside = all.Plan.UnassignedFiles.Count;
        var leftOut = all.Cards.Where(cf => Cards.Any(c => c.IsSelected && c.Name == cf.Card.Name))
            .Sum(cf => cf.Files.Count(f => _excluded.Contains(f.Path)));
        var storage = files > 0 ? _ctx.CheckStorage(bytes) : null;
        WarnIsProblem = blocked || storage?.Blocks == true;
        WarnLine = blocked ? "Choose a folder for the unknown card before you continue."
            : storage?.Blocks == true ? storage.Message
            : leftOut > 0 && outside > 0 ? $"{Format.Files(leftOut)} unticked and {Format.Files(outside)} outside the chosen times won't be copied."
            : leftOut > 0 ? $"{Format.Files(leftOut)} unticked on the timeline won't be copied."
            : outside > 0 ? $"{Format.Files(outside)} outside the chosen times won't be copied."
            : "Nothing is overwritten. Every file is checked after copying.";
        CanReview = files > 0 && !blocked && storage?.Blocks != true && CurrentKinds().Count > 0;
        await RefreshQuickAsync();
    }

    private async Task RefreshQuickAsync()
    {
        var setup = _ctx.Settings.CurrentSetup;
        QuickPills.Clear();
        QuickPills.Add(setup.CardKeys is null ? "All cards" : $"{setup.CardKeys.Count} {(setup.CardKeys.Count == 1 ? "card" : "cards")}");
        QuickPills.Add(setup.Date.Describe(_ctx.Today));
        QuickPills.Add(Format.Kinds(setup.Kinds));
        try
        {
            var p = await _ctx.PreviewAsync(CopyRequest.FromSetup(setup, QuickName()));
            QuickPills.Add(p.Plan.Items.Count > 0 ? $"{Format.Files(p.Plan.Items.Count)} · {Format.Bytes(p.Plan.TotalBytes)}" : "Nothing to copy");
            var quickStorage = p.Plan.Items.Count > 0 ? _ctx.CheckStorage(p.Plan.TotalBytes) : null;
            QuickCanCopy = p.Plan.CanRun && quickStorage?.Blocks != true;
            QuickButton = quickStorage?.Blocks == true ? "Can't copy" : p.Plan.Items.Count > 0 ? "Copy now" : "Nothing to copy";
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException)
        {
            QuickCanCopy = false;
        }
        QuickPills.Add(_ctx.Settings.Template.Name + " folders");
    }

    partial void OnDateIndexChanged(int value)
    {
        // Start the calendar on today rather than an empty box.
        if (value == (int)DateMode.Custom && CustomFrom is null) CustomFrom = _ctx.Today.ToDateTime(TimeOnly.MinValue);
        Recompute();
    }
    partial void OnCustomFromChanged(DateTime? value) => Recompute();
    partial void OnCustomToChanged(DateTime? value) => Recompute();
    partial void OnVideoChanged(bool value) => Recompute();
    partial void OnAudioChanged(bool value) => Recompute();
    partial void OnPhotosChanged(bool value) => Recompute();
    partial void OnProxiesChanged(bool value) => Recompute();

    partial void OnSmartOnChanged(bool value)
    {
        _ctx.Settings.SmartBoxOn = value;
        _ctx.Save();
        if (!value) ShowUnderstood = ShowMiss = false;
    }

    // ---- Smart box -------------------------------------------------------------------------

    [RelayCommand]
    private void ApplySmart() => RunSmart(Query);

    private void RunSmart(string text)
    {
        Query = text;
        var r = RuleParser.Parse(text, _ctx.Today, _ctx.KnownCards());
        if (!r.UnderstoodAnything)
        {
            ShowUnderstood = false;
            ShowMiss = true;
            return;
        }
        ShowMiss = false;
        _loading = true;
        if (r.Date is { } d) SetDate(d);
        if (r.AllCards) foreach (var c in Cards) c.IsSelected = true;
        else if (r.CardKeys is { } keys) foreach (var c in Cards) c.IsSelected = keys.Any(c.Card.MatchesKey);
        if (r.Kinds is { } k) SetKinds(k);
        if (r.ProjectName is { } name) ProjectName = name;
        _loading = false;
        if (r.Splits is not null)
        {
            TimelineOn = false; // typed times win over the timeline
            SetSplits(r.Splits);
        }
        else if (!TimelineOn)
        {
            SetSplits(null);
        }
        SplitNote = "";

        var cardsText = Cards.All(c => c.IsSelected) ? "All cards" : string.Join(", ", Cards.Where(c => c.IsSelected).Select(c => c.Name));
        var projectText = _splits is null ? $"Project “{Name()}”"
            : string.Join(", ", _splits.Select(sp => $"“{sp.ProjectName ?? Name()}” {sp.Window.Describe()}"));
        Understood = $"{(cardsText.Length == 0 ? "No cards" : cardsText)} · {CurrentDate().Describe(_ctx.Today)} · {Format.Kinds(CurrentKinds())} · {projectText}";
        ShowUnderstood = true;
        Recompute();
    }

    private void SetDate(DateRule d)
    {
        switch (d.Kind)
        {
            case DateRuleKind.Today: DateIndex = 0; break;
            case DateRuleKind.Yesterday: DateIndex = 1; break;
            case DateRuleKind.LastDays when d.Days == 3: DateIndex = 2; break;
            default:
                var (from, to) = d.Resolve(_ctx.Today);
                CustomFrom = from.ToDateTime(TimeOnly.MinValue);
                CustomTo = to.ToDateTime(TimeOnly.MinValue);
                DateIndex = 3;
                break;
        }
    }

    private void SetKinds(IEnumerable<FileKind> kinds)
    {
        var set = kinds.ToHashSet();
        Video = set.Contains(FileKind.Video);
        Audio = set.Contains(FileKind.Audio);
        Photos = set.Contains(FileKind.Photo);
        Proxies = set.Contains(FileKind.Proxy);
    }

    // ---- Setups ----------------------------------------------------------------------------

    private void ApplySetupFields(Setup setup)
    {
        SetDate(setup.Date);
        SetKinds(setup.Kinds);
    }

    private void LoadSetupChips()
    {
        Setups.Clear();
        foreach (var s in _ctx.Settings.Setups) Setups.Add(new SetupChip(s.Name, UseSetup));
    }

    private void UseSetup(SetupChip chip)
    {
        var setup = _ctx.Settings.FindSetup(chip.Name);
        if (setup is null) return;
        foreach (var c in Setups) c.IsActive = c == chip;
        _loading = true;
        ApplySetupFields(setup);
        foreach (var c in Cards) c.IsSelected = setup.CardKeys is null || setup.CardKeys.Any(c.Card.MatchesKey);
        _loading = false;
        ShowUnderstood = false;
        Recompute();
    }

    [RelayCommand]
    private void SaveSetup()
    {
        var name = NewSetupName.Trim();
        if (name.Length == 0) name = $"My setup {_ctx.Settings.Setups.Count}";
        // Saved setups remember devices by profile (so tomorrow's card matches), unknown cards by name.
        IReadOnlyList<string>? keys = Cards.All(c => c.IsSelected) ? null
            : Cards.Where(c => c.IsSelected).Select(c => c.Card.Profile?.Id ?? c.Card.CardId).Distinct().ToList();
        _ctx.Settings.SaveSetup(new Setup(name, keys, CurrentDate(), CurrentKinds().Order().ToList()));
        _ctx.Save();
        NewSetupName = "";
        LoadSetupChips();
        foreach (var c in Setups) c.IsActive = c.Name == name;
    }

    // ---- Navigation ------------------------------------------------------------------------

    [RelayCommand]
    private async Task QuickCopyAsync()
    {
        var p = await _ctx.PreviewAsync(CopyRequest.FromSetup(_ctx.Settings.CurrentSetup, QuickName()));
        if (p.Plan.CanRun) _ctx.Shell.GoCopying(p);
    }

    [RelayCommand(CanExecute = nameof(CanReview))]
    private async Task ReviewAsync()
    {
        var p = await _ctx.PreviewAsync(CurrentRequest(SelectedKeys()));
        if (p.Plan.CanRun) _ctx.Shell.GoReview(p);
        else Recompute();
    }

    [RelayCommand] private void BackToReady() => _ctx.Shell.GoReady();
    [RelayCommand] private void OpenSettings() => _ctx.Shell.GoSettings();
    [RelayCommand] private void EditFolders() => _ctx.Shell.GoSettings("Folders");
}
