using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;

namespace ShootDrop.App.ViewModels;

/// <summary>One clip in a session's list: tick to copy it, or "Start a new group here".</summary>
public sealed partial class TimelineClipItem(string path, string time, string name, string card, string size, bool canSplit,
    bool included, Action split, Action<string, bool> setIncluded) : ObservableObject
{
    public string Path { get; } = path;
    public string Time { get; } = time;
    public string Name { get; } = name;
    public string Card { get; } = card;
    public string Size { get; } = size;
    public bool CanSplit { get; } = canSplit;
    public IRelayCommand SplitHereCommand { get; } = new RelayCommand(split);

    [ObservableProperty] private bool _included = included;

    partial void OnIncludedChanged(bool value) => setIncluded(Path, value);
}

/// <summary>One group of clips on the timeline (usually one location), with the project it goes to.</summary>
public sealed partial class TimelineSession : ObservableObject
{
    private readonly Action<int, string> _renamed;

    private readonly Action<IEnumerable<string>, bool> _setManyIncluded;
    private readonly long _totalBytes;
    private bool _bulk;

    public TimelineSession(int index, ClipSession session, string name, IBrush color, double left, double width,
        bool canMergeNext, Action<int> merge, Action<int, string> renamed, Func<int, int, bool> canSplit, Action<int, int> split,
        Func<string, bool> isIncluded, Action<IEnumerable<string>, bool> setManyIncluded)
    {
        _setManyIncluded = setManyIncluded;
        _totalBytes = session.TotalBytes;
        _cardNames = string.Join(", ", session.CardNames);
        Index = index;
        _name = name;
        _renamed = renamed;
        Color = color;
        BarMargin = new Avalonia.Thickness(left, 0, 0, 0);
        BarWidth = Math.Max(width, 6);
        TimeText = session.Clips.Count == 1
            ? TimeWindow.Format(TimeOnly.FromDateTime(session.First))
            : $"{TimeWindow.Format(TimeOnly.FromDateTime(session.First))} – {TimeWindow.Format(TimeOnly.FromDateTime(session.Last))}";

        CanMergeNext = canMergeNext;
        MergeNextCommand = new RelayCommand(() => merge(index));
        _filesText = Format.Files(session.Clips.Count);
        for (var i = 0; i < session.Clips.Count; i++)
        {
            var clipIndex = i;
            var c = session.Clips[i];
            var item = new TimelineClipItem(c.File.Path, TimeWindow.Format(TimeOnly.FromDateTime(c.Time)), c.File.Name, c.CardName,
                Format.Bytes(c.File.Size), canSplit(index, clipIndex), isIncluded(c.File.Path), () => split(index, clipIndex),
                (path, on) => { if (!_bulk) setManyIncluded([path], on); });
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TimelineClipItem.Included) && !_bulk) RefreshIncluded();
            };
            Clips.Add(item);
        }
        _sizes = session.Clips.ToDictionary(c => c.File.Path, c => c.File.Size);
        RefreshIncluded();
    }

    private readonly string _cardNames;
    private Dictionary<string, long> _sizes = [];

    /// <summary>"46 files · 14.1 GB · LUMIX, DJI", or "40 of 46 files · …" when some are unticked.</summary>
    [ObservableProperty] private string _summary = "";

    /// <summary>Group tick box: true = every file copies, false = none, null = some.</summary>
    [ObservableProperty] private bool? _included = true;

    partial void OnIncludedChanged(bool? value)
    {
        if (_bulk || value is null) return;
        _bulk = true;
        foreach (var clip in Clips) clip.Included = value.Value;
        _bulk = false;
        _setManyIncluded(Clips.Select(c => c.Path), value.Value);
        RefreshIncluded();
    }

    private void RefreshIncluded()
    {
        var on = Clips.Where(c => c.Included).ToList();
        _bulk = true;
        Included = on.Count == Clips.Count ? true : on.Count == 0 ? false : null;
        _bulk = false;
        var bytes = on.Sum(c => _sizes.GetValueOrDefault(c.Path));
        Summary = on.Count == Clips.Count
            ? $"{Format.Files(Clips.Count)} · {Format.Bytes(_totalBytes)} · {_cardNames}"
            : $"{on.Count} of {Format.Files(Clips.Count)} · {Format.Bytes(bytes)} · {_cardNames} · {(on.Count == 0 ? "not copied" : "some left out")}";
    }

    public int Index { get; }
    public IBrush Color { get; }
    public Avalonia.Thickness BarMargin { get; }
    public double BarWidth { get; }
    public string TimeText { get; }
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
    [ObservableProperty] private bool _hasTimelineClips;
    [ObservableProperty] private string _timelineNote = "";
    [ObservableProperty] private string _timelineStart = "";
    [ObservableProperty] private string _timelineEnd = "";

    private IReadOnlyList<CardFiles> _timelineCards = [];

    /// <summary>Card files the person unticked on the timeline. They stay on the card.</summary>
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);

    private IReadOnlySet<string>? ExcludedFiles => _excluded.Count > 0 ? _excluded.ToHashSet(StringComparer.Ordinal) : null;

    private void SetIncluded(IEnumerable<string> paths, bool included)
    {
        foreach (var p in paths)
        {
            if (included) _excluded.Remove(p);
            else _excluded.Add(p);
        }
        Recompute();
    }

    /// <summary>The slider: a pause at least this long starts a new group (15 min to 4 h, in 15-minute steps).</summary>
    public double BreakMinutes
    {
        get => _ctx.Settings.TimelineBreak.TotalMinutes;
        set
        {
            var minutes = (int)Math.Clamp(Math.Round(value / 15) * 15, 15, 240);
            if (minutes == _ctx.Settings.TimelineBreakMinutes) return;
            _ctx.Settings.TimelineBreakMinutes = minutes;
            _ctx.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(BreakText));
            _sessionSignature = ""; // regroup with the new break
            UpdateTimeline(_timelineCards);
            Recompute();
        }
    }

    public string BreakText
    {
        get
        {
            var t = _ctx.Settings.TimelineBreak;
            return t.TotalMinutes < 60 ? $"{t.TotalMinutes:0} min"
                : t.Minutes == 0 ? $"{t.Hours} h"
                : $"{t.Hours} h {t.Minutes} min";
        }
    }

    /// <summary>Called after each scan with the selected cards' clips. Keeps the user's grouping while the clips are the same.</summary>
    private bool UpdateTimeline(IReadOnlyList<CardFiles> selectedCards)
    {
        _timelineCards = selectedCards;
        var sessions = ClipTimeline.Build(selectedCards, _ctx.Settings.TimelineBreak);
        var signature = _ctx.Settings.TimelineBreakMinutes + "|" + ClipTimeline.Signature(sessions);
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
        HasTimelineClips = sessions.Count > 0;
        TimelineNote = sessions.Count switch
        {
            0 when !IsSingleDayChoice() => "Pick one day to see its timeline.",
            0 => "No clips on this day with these choices.",
            1 => $"One group: no pause of {BreakText} or more. Move the slider left to split shorter pauses.",
            var n => $"{n} groups, split wherever recording paused for {BreakText} or more.",
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
                (si, ci) => ClipTimeline.CanSplitBefore(_sessions, si, ci), SplitSession,
                path => !_excluded.Contains(path), SetIncluded));
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
