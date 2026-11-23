using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.Core;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Settings;

namespace ShootDrop.App.ViewModels;

/// <summary>One row in the card list.</summary>
public sealed record CardRow(string Name, string Device, string Stat, bool InSetup);

/// <summary>
/// The compact "Ready to copy" window (FR-22): current setup, cards, project name, Copy now.
/// All copying is done by ShootDropService; this class only shows state.
/// </summary>
public sealed partial class ReadyViewModel : ObservableObject
{
    private readonly ShootDropService _service;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _watch;
    private CopyPreview? _preview;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _projectName;
    [ObservableProperty] private string? _selectedSetup;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isCopying;
    [ObservableProperty] private bool _needsProjectsRoot;

    public ObservableCollection<CardRow> Cards { get; } = [];
    public ObservableCollection<string> SetupNames { get; } = [];

    public ReadyViewModel(ShootDropService service, SettingsStore store)
    {
        _service = service;
        _store = store;
        _settings = store.Load();
        _projectName = ShootDropService.DefaultProjectName(service.Today);

        SetupNames.Add("Last used");
        foreach (var s in _settings.Setups) SetupNames.Add(s.Name);
        _selectedSetup = SetupNames[0];

        Refresh();
        // Cards come and go; a cheap re-scan every 3 s keeps the list live (FR-01).
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _watch.Tick += (_, _) => { if (!IsCopying) Refresh(); };
        _watch.Start();
    }

    partial void OnSelectedSetupChanged(string? value) => Refresh();

    private Setup CurrentSetup() =>
        SelectedSetup is null or "Last used"
            ? _settings.CurrentSetup
            : _settings.FindSetup(SelectedSetup) ?? _settings.CurrentSetup;

    [RelayCommand]
    private void Refresh()
    {
        NeedsProjectsRoot = string.IsNullOrWhiteSpace(_settings.ProjectsRoot);
        Cards.Clear();
        _preview = null;

        if (NeedsProjectsRoot)
        {
            Summary = "";
            Status = "Set where projects go: run `shootdrop config --root <folder>` (Settings screen comes next).";
            CopyNowCommand.NotifyCanExecuteChanged();
            return;
        }

        var setup = CurrentSetup();
        var detected = _service.DetectCards(_settings);
        var request = CopyRequest.FromSetup(setup, string.IsNullOrWhiteSpace(ProjectName) ? "Untitled" : ProjectName);
        _preview = _service.Preview(request, _settings, detected);

        foreach (var card in detected)
        {
            var items = _preview.Plan.Items.Where(i => i.CardName == card.Name).ToList();
            var inSetup = items.Count > 0 || _preview.Plan.Unrouted.Contains(card);
            var stat = items.Count > 0
                ? $"{items.Count} files · {Human(items.Sum(i => i.Source.Size))}"
                : _preview.Plan.Unrouted.Contains(card) ? "Choose a folder (More options)" : "Nothing for these dates";
            Cards.Add(new CardRow(card.Name, card.Profile?.Name ?? "Unknown device", stat, inSetup));
        }

        Summary = $"{setup.Date.Describe(_service.Today)} · {string.Join(" + ", setup.Kinds)}";
        Status = detected.Count == 0
            ? "Insert a card to start."
            : _preview.Plan.CanRun ? $"{_preview.Plan.Items.Count} files, {Human(_preview.Plan.TotalBytes)}" : "Nothing to copy yet.";
        CopyNowCommand.NotifyCanExecuteChanged();
    }

    private bool CanCopyNow() => !IsCopying && _preview is { Plan.CanRun: true } && !string.IsNullOrWhiteSpace(ProjectName);

    [RelayCommand(CanExecute = nameof(CanCopyNow))]
    private async Task CopyNowAsync()
    {
        // Rebuild with the final project name.
        var request = CopyRequest.FromSetup(CurrentSetup(), ProjectName);
        var preview = _service.Preview(request, _settings);
        if (!preview.Plan.CanRun) { Refresh(); return; }

        IsCopying = true;
        CopyNowCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        var progress = new Progress<CopyProgress>(p =>
        {
            Progress = p.Fraction * 100;
            Status = $"{p.Fraction:P0} · file {p.FileNumber} of {p.FileCount} · {p.CurrentFile}";
        });

        try
        {
            var result = await _service.RunAsync(preview, _settings, progress, _cts.Token);
            _store.Save(_settings);
            Status = result.Failed == 0
                ? $"Done: {result.Copied} copied and verified, {result.Skipped} already there."
                : $"{result.Failed} failed. Run again; finished files are skipped.";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled. Finished files are kept.";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Status = "Error: " + e.Message;
        }
        finally
        {
            IsCopying = false;
            _cts.Dispose();
            _cts = null;
            CopyNowCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    partial void OnProjectNameChanged(string value) => CopyNowCommand.NotifyCanExecuteChanged();

    private static string Human(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[i];
    }
}
