using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;

namespace ShootDrop.App.ViewModels;

/// <summary>One card line: name, device, what it will copy.</summary>
public sealed record CardRow(string Name, string Device, string Stat, bool Included, bool NeedsFolder)
{
    public bool Excluded => !Included;
}

/// <summary>The compact "Ready to copy" window (FR-22): one click, last setup (FR-20).</summary>
public sealed partial class ReadyPageViewModel : PageViewModel
{
    private const string LastUsed = "Last used";
    private readonly ShootDropContext _ctx;
    private CopyPreview? _preview;
    private int _version;

    [ObservableProperty] private string? _selectedSetup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyNowCommand))]
    private string _projectName;

    [ObservableProperty] private string _buttonText = "Copy now";
    [ObservableProperty] private string _status = "Looking for cards…";
    [ObservableProperty] private bool _hasStatus = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyNowCommand))]
    private bool _canCopy;

    public ReadyPageViewModel(ShootDropContext ctx)
    {
        _ctx = ctx;
        _projectName = ctx.DefaultProjectName;
        SetupNames.Add(LastUsed);
        foreach (var s in ctx.Settings.Setups) SetupNames.Add(s.Name);
        _selectedSetup = LastUsed;
    }

    public override bool Compact => true;

    public ObservableCollection<string> SetupNames { get; } = [];

    // ---- Which day(s): the setup's own dates, a quick choice, or picked on a calendar ---------------
    public string[] DateOptions { get; } = ["Setup's dates", "Today", "Yesterday", "Last 3 days", "Pick dates…"];
    private const int PickIndex = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPicking))]
    private int _dateIndex;

    [ObservableProperty] private DateTime? _pickFrom;
    [ObservableProperty] private DateTime? _pickTo;

    public bool IsPicking => DateIndex == PickIndex;

    partial void OnDateIndexChanged(int value)
    {
        if (value == PickIndex && PickFrom is null) PickFrom = _ctx.Today.ToDateTime(TimeOnly.MinValue);
        _ = RefreshAsync();
    }

    partial void OnPickFromChanged(DateTime? value) => _ = RefreshAsync();

    partial void OnPickToChanged(DateTime? value) => _ = RefreshAsync();

    /// <summary>The chosen setup with the date choice from this window applied.</summary>
    private Setup EffectiveSetup()
    {
        var setup = CurrentSetup();
        DateRule? date = DateIndex switch
        {
            1 => DateRule.Today,
            2 => DateRule.Yesterday,
            3 => DateRule.LastDays(3),
            PickIndex => Picked(),
            _ => null,
        };
        return date is null ? setup : setup with { Date = date };
    }

    private DateRule Picked()
    {
        var from = DateOnly.FromDateTime(PickFrom ?? _ctx.Today.ToDateTime(TimeOnly.MinValue));
        var to = PickTo is { } t ? DateOnly.FromDateTime(t) : from;
        return to < from ? DateRule.Between(to, from) : DateRule.Between(from, to);
    }
    public ObservableCollection<CardRow> Cards { get; } = [];
    public ObservableCollection<string> Pills { get; } = [];

    public override void OnShown()
    {
        _ctx.Monitor.Changed += OnCardsChanged;
        _ = RefreshAsync();
    }

    public override void OnHidden() => _ctx.Monitor.Changed -= OnCardsChanged;

    private void OnCardsChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    partial void OnSelectedSetupChanged(string? value) => _ = RefreshAsync();

    private Setup CurrentSetup() =>
        SelectedSetup is null or LastUsed
            ? _ctx.Settings.CurrentSetup
            : _ctx.Settings.FindSetup(SelectedSetup) ?? _ctx.Settings.CurrentSetup;

    private async Task RefreshAsync()
    {
        var version = ++_version;
        var setup = EffectiveSetup();
        Pills.Clear();
        Pills.Add(setup.Date.Describe(_ctx.Today));
        Pills.Add(Format.Kinds(setup.Kinds));
        Pills.Add(_ctx.Settings.Template.Name + " folders");

        CopyPreview preview;
        try
        {
            preview = await _ctx.PreviewAsync(CopyRequest.FromSetup(setup, NameOrDefault()));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException)
        {
            Status = e.Message;
            HasStatus = true;
            CanCopy = false;
            return;
        }
        if (version != _version) return; // a newer refresh started

        _preview = preview;
        var plan = preview.Plan;
        Cards.Clear();
        foreach (var card in _ctx.Monitor.Cards)
        {
            var items = plan.Items.Where(i => i.CardName == card.Name).ToList();
            var unrouted = plan.Unrouted.Contains(card);
            var included = items.Count > 0 || unrouted;
            var stat = items.Count > 0 ? $"{Format.Files(items.Count)} · {Format.Bytes(items.Sum(i => i.Source.Size))}"
                : unrouted ? "Choose its folder in More options"
                : setup.CardKeys is not null && !setup.CardKeys.Any(card.MatchesKey) ? "Not in this setup"
                : "Nothing for these dates";
            Cards.Add(new CardRow(card.Name, card.Profile?.Name ?? "Unknown device", stat, included, unrouted));
        }

        CanCopy = plan.CanRun;
        ButtonText = plan.Items.Count > 0 ? $"Copy now · {Format.Bytes(plan.TotalBytes)}" : "Copy now";
        (Status, HasStatus) = _ctx.Monitor.Cards.Count == 0 ? ("Insert a card to start. ShootDrop watches for new cards.", true)
            : plan.Unrouted.Count > 0 ? ("An unknown card needs a folder. Open More options to choose one.", true)
            : plan.Items.Count == 0 ? ("Nothing recorded on these dates. Pick another setup or More options.", true)
            : ("", false);
    }

    private string NameOrDefault() => string.IsNullOrWhiteSpace(ProjectName) ? _ctx.DefaultProjectName : ProjectName;

    private bool CanCopyNow() => CanCopy && !string.IsNullOrWhiteSpace(ProjectName);

    [RelayCommand(CanExecute = nameof(CanCopyNow))]
    private async Task CopyNowAsync()
    {
        var preview = await _ctx.PreviewAsync(CopyRequest.FromSetup(EffectiveSetup(), ProjectName.Trim()));
        if (!preview.Plan.CanRun)
        {
            await RefreshAsync();
            return;
        }
        _ctx.Shell.GoCopying(preview);
    }

    [RelayCommand]
    private void MoreOptions() => _ctx.Shell.GoMain();

    [RelayCommand]
    private void OpenSettings() => _ctx.Shell.GoSettings();
}
