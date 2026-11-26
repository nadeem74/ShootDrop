using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;

namespace ShootDrop.App.ViewModels;

/// <summary>One clip in a session's list, with "Start a new group here".</summary>
public sealed class TimelineClipItem(string time, string name, string card, bool canSplit, Action split)
{
    public string Time { get; } = time;
    public string Name { get; } = name;
    public string Card { get; } = card;
    public bool CanSplit { get; } = canSplit;
    public IRelayCommand SplitHereCommand { get; } = new RelayCommand(split);
}

/// <summary>One group of clips on the timeline (usually one location), with the project it goes to.</summary>
public sealed partial class TimelineSession : ObservableObject
{
    private readonly Action<int, string> _renamed;

    public TimelineSession(int index, ClipSession session, string name, IBrush color, double left, double width,
        bool canMergeNext, Action<int> merge, Action<int, string> renamed, Func<int, int, bool> canSplit, Action<int, int> split)
    {
        Index = index;
        _name = name;
        _renamed = renamed;
        Color = color;
        BarMargin = new Avalonia.Thickness(left, 0, 0, 0);
        BarWidth = Math.Max(width, 6);
        TimeText = session.Clips.Count == 1
            ? TimeWindow.Format(TimeOnly.FromDateTime(session.First))
            : $"{TimeWindow.Format(TimeOnly.FromDateTime(session.First))} – {TimeWindow.Format(TimeOnly.FromDateTime(session.Last))}";
        Summary = $"{Format.Files(session.Clips.Count)} · {Format.Bytes(session.TotalBytes)} · {string.Join(", ", session.CardNames)}";
        CanMergeNext = canMergeNext;
        MergeNextCommand = new RelayCommand(() => merge(index));
        _filesText = Format.Files(session.Clips.Count);
        for (var i = 0; i < session.Clips.Count; i++)
        {
            var clipIndex = i;
            var c = session.Clips[i];
            Clips.Add(new TimelineClipItem(TimeWindow.Format(TimeOnly.FromDateTime(c.Time)), c.File.Name, c.CardName,
                canSplit(index, clipIndex), () => split(index, clipIndex)));
        }
    }

    public int Index { get; }
    public IBrush Color { get; }
    public Avalonia.Thickness BarMargin { get; }
    public double BarWidth { get; }
    public string TimeText { get; }
    public string Summary { get; }
    public bool CanMergeNext { get; }
    public IRelayCommand MergeNextCommand { get; }
    private readonly string _filesText;
    public string ShowClipsText => ShowClips ? "Hide files" : $"Show {_filesText}";
    public ObservableCollection<TimelineClipItem> Clips { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _showClips;

    partial void OnNameChanged(string value) => _renamed(Index, value);

    partial void OnShowClipsChanged(bool value) => OnPropertyChanged(nameof(ShowClipsText));

    [RelayCommand] private void ToggleClips() => ShowClips = !ShowClips;
}

/// <summary>
/// The timeline: the chosen day's clips grouped where recording paused, each group going to its own project.
/// The user names the groups, merges two, or starts a new group at any clip. No times to type.
/// </summary>
public sealed partial class MainPageViewModel
{
    public const double TimelineWidth = 640;

    private static readonly IBrush[] Palette =
        new[] { "#0A64D6", "#E07A1F", "#2E9E5B", "#9B51E0", "#D64545", "#1FA2B8" }.Select(c => (IBrush)SolidColorBrush.Parse(c)).ToArray();

    private IReadOnlyList<ClipSession> _sessions = [];
    private List<string> _sessionNames = [];
    private string _sessionSignature = "";

    public ObservableCollection<TimelineSession> Sessions { get; } = [];
    [ObservableProperty] private bool _timelineOn;
    [ObservableProperty] private bool _hasTimeline;
    [ObservableProperty] private string _timelineNote = "";
    [ObservableProperty] private string _timelineStart = "";
    [ObservableProperty] private string _timelineEnd = "";

    /// <summary>Called after each scan with the selected cards' clips. Keeps the user's grouping while the clips are the same.</summary>
    private bool UpdateTimeline(IEnumerable<CardFiles> selectedCards)
    {
        var sessions = ClipTimeline.Build(selectedCards);
        var signature = ClipTimeline.Signature(sessions);
        if (signature == _sessionSignature) return false;

        _sessionSignature = signature;
        _sessionNames = sessions.Select((_, i) => $"Part {i + 1}").ToList();
        ShowSessions(sessions);
        return TimelineOn;
    }

    private void ShowSessions(IReadOnlyList<ClipSession> sessions)
    {
        _sessions = sessions;
        Sessions.Clear();
        HasTimeline = sessions.Count > 0;
        TimelineNote = sessions.Count switch
        {
            0 when !IsSingleDayChoice() => "Pick one day to see its timeline.",
            0 => "No clips on this day with these choices.",
            1 => "One session: everything was recorded without a long break.",
            var n => $"{n} sessions, split wherever recording paused for an hour or more.",
        };
        if (sessions.Count == 0)
        {
            TimelineStart = TimelineEnd = "";
            return;
        }

        var start = sessions[0].First;
        var end = sessions[^1].Last;
        var span = Math.Max((end - start).TotalMinutes, 1);
        TimelineStart = TimeWindow.Format(TimeOnly.FromDateTime(start));
        TimelineEnd = TimeWindow.Format(TimeOnly.FromDateTime(end));
        var usable = TimelineWidth - 6;
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var left = (s.First - start).TotalMinutes / span * usable;
            var width = (s.Last - s.First).TotalMinutes / span * usable;
            Sessions.Add(new TimelineSession(i, s, _sessionNames[i], Palette[i % Palette.Length], left, width,
                i < sessions.Count - 1, MergeSession, RenameSession,
                (si, ci) => ClipTimeline.CanSplitBefore(_sessions, si, ci), SplitSession));
        }
        if (TimelineOn) UseTimelineSplits();
    }

    private bool IsSingleDayChoice()
    {
        var (from, to) = CurrentDate().Resolve(_ctx.Today);
        return from == to;
    }

    /// <summary>Typed splits ("till 1 pm into A…") are listed only when the timeline isn't in charge.</summary>
    public bool ShowTypedSplits => HasSplits && !TimelineOn;

    partial void OnHasSplitsChanged(bool value) => OnPropertyChanged(nameof(ShowTypedSplits));

    partial void OnTimelineOnChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTypedSplits));
        if (value)
        {
            UseTimelineSplits();
            ShowUnderstood = false; // typed splits give way to the timeline
        }
        else
        {
            SetSplits(null);
        }
        Recompute();
    }

    private void UseTimelineSplits()
    {
        if (_sessions.Count == 0)
        {
            SetSplits(null);
            return;
        }
        SetSplits(ClipTimeline.ToSplits(_sessions, _sessionNames.Select((n, i) => n.Trim().Length > 0 ? n : $"Part {i + 1}").ToList()));
    }

    private void RenameSession(int index, string name)
    {
        if (index < _sessionNames.Count) _sessionNames[index] = name;
        if (TimelineOn) UseTimelineSplits();
    }

    private void MergeSession(int index)
    {
        if (index >= _sessions.Count - 1) return;
        _sessionNames.RemoveAt(index + 1);
        Renumber();
        ShowSessions(ClipTimeline.MergeWithNext(_sessions, index));
        Recompute();
    }

    /// <summary>Names the user hasn't changed ("Part 3") follow the groups' new order: Part 1, Part 2…</summary>
    private void Renumber()
    {
        for (var i = 0; i < _sessionNames.Count; i++)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(_sessionNames[i], @"^Part \d+$|^$")) _sessionNames[i] = $"Part {i + 1}";
        }
    }

    private void SplitSession(int index, int clipIndex)
    {
        var updated = ClipTimeline.SplitBefore(_sessions, index, clipIndex);
        if (ReferenceEquals(updated, _sessions)) return;
        _sessionNames.Insert(index + 1, "");
        Renumber();
        ShowSessions(updated);
        Recompute();
    }
}
