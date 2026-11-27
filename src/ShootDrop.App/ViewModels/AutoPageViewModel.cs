using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core.Agent;
using ShootDrop.Core.Autopilot;

namespace ShootDrop.App.ViewModels;

/// <summary>One recording session on the autopilot screen: tick to copy, name to change.</summary>
public sealed partial class AutoSessionRow(int index, AutoSession session) : ObservableObject
{
    public int Index { get; } = index;
    public AutoSession Session { get; } = session;
    public string Time { get; } = $"{session.First:HH:mm} – {session.Last:HH:mm}";
    public string Detail { get; } = $"{session.Day:ddd d MMM} · {session.ClipCount} clips · {Format.Bytes(session.TotalBytes)} · {string.Join(", ", session.Session.CardNames)}";
    [ObservableProperty] private bool _included = true;
    /// <summary>Starts empty: the person names each project. Left blank, it gets the date name shown as the hint.</summary>
    [ObservableProperty] private string _name = "";
    public string Placeholder { get; } = session.DefaultName;
    public string Label { get; } = $"Project {index + 1}";
    [ObservableProperty] private string _source = "";
}

/// <summary>
/// The autopilot screen (FR-26, ADR-008): opens by itself when cards go in. Shows the new recording sessions in time
/// order with ready names and the free space; one Start, then copy, verify and report without further questions.
/// </summary>
public sealed partial class AutoPageViewModel : PageViewModel
{
    private readonly ShootDropContext ctx;
    private AutoProposal? _proposal;

    public AutoPageViewModel(ShootDropContext context)
    {
        ctx = context;
        _note = context.Settings.ShootNote;
    }

    public override bool Compact => true;
    public ObservableCollection<AutoSessionRow> Sessions { get; } = [];
    [ObservableProperty] private string _headline = "Looking at the cards…";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _statusIsProblem;
    [ObservableProperty] private bool _hasSessions;
    [ObservableProperty] private string _note;
    [ObservableProperty] private bool _naming;

    /// <summary>True when an Anthropic API key is set up, so Claude can suggest names (optional).</summary>
    public bool ClaudeAvailable => AnthropicModel.FindKey(SettingsDirectory) is not null;
    public string NameButtonText => ClaudeAvailable ? "Name from note (with Claude)" : "Name from note";
    private string SettingsDirectory => Path.GetDirectoryName(ctx.Store.Path)!;

    public override void OnShown() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        var cards = ctx.Monitor.Cards;
        try
        {
            _proposal = await Task.Run(() => new AutoCopy(ctx.Service).Propose(ctx.Settings, cards));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            (Headline, Status, StatusIsProblem) = ("Couldn't read the cards", e.Message, true);
            return;
        }

        Sessions.Clear();
        for (var i = 0; i < _proposal.Sessions.Count; i++) Sessions.Add(new AutoSessionRow(i, _proposal.Sessions[i]));
        HasSessions = Sessions.Count > 0;
        Headline = Sessions.Count switch
        {
            0 when cards.Count == 0 => "Insert a card",
            0 => "Nothing new on the cards",
            1 => "1 shoot found. What's the project called?",
            _ => $"{Sessions.Count} shoots found. Name each project",
        };
        if (!HasSessions)
        {
            (Status, StatusIsProblem) = (cards.Count == 0 ? "ShootDrop watches for cards and opens this by itself." : "Everything on these cards has been copied already.", false);
            return;
        }
        if (Note.Trim().Length > 0) Apply(SessionNamer.FromNote(Facts()));
        CheckSpace();
        if (_proposal.UnknownCards.Count > 0)
            (Status, StatusIsProblem) = ($"{string.Join(", ", _proposal.UnknownCards.Select(c => c.Name))}: unknown device. Open More options to choose its folder.", true);
    }

    private NamingFacts Facts() => NamingFacts.For(_proposal!, Note.Trim(), ctx.Settings.ProjectsRoot);

    private void Apply(NamingResult result)
    {
        foreach (var row in Sessions)
        {
            var s = result.Names.FirstOrDefault(n => n.Session == row.Index);
            row.Name = s?.Name ?? "";
            row.Source = s is null ? "" : "from " + s.Source;
        }
    }

    private bool CheckSpace()
    {
        var bytes = Sessions.Where(s => s.Included).Sum(s => s.Session.TotalBytes);
        var storage = ctx.CheckStorage(bytes);
        (Status, StatusIsProblem) = (storage.Message, storage.Blocks);
        return !storage.Blocks;
    }

    [RelayCommand]
    private async Task NameFromNoteAsync()
    {
        if (_proposal is null) return;
        ctx.Settings.ShootNote = Note.Trim();
        ctx.Save();
        var facts = Facts();
        var result = SessionNamer.FromNote(facts);
        if (ClaudeAvailable && facts.Note.Length > 0)
        {
            Naming = true;
            try
            {
                var key = AnthropicModel.FindKey(SettingsDirectory)!;
                result = await new NamingAgent(new AnthropicModel(key, ctx.Settings.AgentModel)).RunAsync(facts);
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                ctx.Shell.ShowToast("Claude couldn't help this time, used the note rules: " + e.Message);
            }
            finally
            {
                Naming = false;
            }
        }
        Apply(result);
        AgentTrace.Save(SettingsDirectory, result);
    }

    [RelayCommand]
    private void Start()
    {
        if (_proposal is null || !CheckSpace()) return;
        var skipped = Sessions.Where(s => !s.Included).Select(s => s.Index).ToHashSet();
        try
        {
            var preview = new AutoCopy(ctx.Service).Preview(_proposal, Sessions.Select(s => (string?)s.Name).ToList(), skipped, ctx.Settings);
            if (!preview.Plan.CanRun)
            {
                (Status, StatusIsProblem) = ("Choose a folder for " + string.Join(", ", preview.Plan.Unrouted.Select(c => c.Name)) + " in More options first.", true);
                return;
            }
            ctx.Shell.GoCopying(preview, autopilot: true);
        }
        catch (InvalidOperationException e)
        {
            (Status, StatusIsProblem) = (e.Message, true);
        }
    }

    [RelayCommand]
    private void MoreOptions() => ctx.Shell.GoMain();

    [RelayCommand]
    private void Refresh() => _ = LoadAsync();
}
