using System.Globalization;

namespace ShootDrop.Core.Media;

/// <summary>
/// A time of day range, compared to the minute: "till 1 pm" includes a clip that stopped at 1:00:40 pm.
/// Null <see cref="From"/> means from the start of the day; null <see cref="To"/> means to the end of it.
/// A clip's time is when its file was last written, which on cameras is when recording stopped.
/// </summary>
public sealed record TimeWindow(TimeOnly? From, TimeOnly? To)
{
    public static TimeWindow AllDay { get; } = new(null, null);

    public bool Contains(DateTime when) => Contains(TimeOnly.FromDateTime(when));

    public bool Contains(TimeOnly t)
    {
        var m = new TimeOnly(t.Hour, t.Minute);
        return (From is null || m >= From) && (To is null || m <= To);
    }

    /// <summary>"till 1:00 pm", "1:01 pm – 6:00 pm", "after 6:01 pm", "all day".</summary>
    public string Describe() => (From, To) switch
    {
        (null, null) => "all day",
        (null, { } to) => $"till {Format(to)}",
        ({ } from, null) => $"after {Format(from)}",
        ({ } from, { } to) => $"{Format(from)} – {Format(to)}",
    };

    public static string Format(TimeOnly t) =>
        t.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
}

/// <summary>
/// One part of a split copy: clips whose time falls in <see cref="Window"/> go to <see cref="ProjectName"/>.
/// A null project name means "the project named in the main box".
/// </summary>
public sealed record ProjectSplit(string? ProjectName, TimeWindow Window);

public static class ProjectSplits
{
    /// <summary>
    /// Fills open ends from the neighbours, so "till 1 pm A, till 6 pm B, till 11 pm C" becomes
    /// A: till 1:00 pm, B: 1:01 – 6:00 pm, C: 6:01 – 11:00 pm. An explicit time is never changed.
    /// </summary>
    public static IReadOnlyList<ProjectSplit> Chain(IReadOnlyList<ProjectSplit> splits)
    {
        var from = splits.Select(s => s.Window.From).ToArray();
        var to = splits.Select(s => s.Window.To).ToArray();
        var explicitFrom = splits.Select(s => s.Window.From is not null).ToArray();

        // A part that only says where it ends starts right after the previous part ends.
        for (var i = 1; i < splits.Count; i++)
        {
            if (!explicitFrom[i] && to[i - 1] is { } prevTo && prevTo < new TimeOnly(23, 59))
                from[i] = prevTo.AddMinutes(1);
        }

        // A part that only says where it starts runs until the next part starts.
        for (var i = 0; i < splits.Count - 1; i++)
        {
            if (to[i] is null && from[i + 1] is { } nextFrom && nextFrom > TimeOnly.MinValue)
                to[i] = nextFrom.AddMinutes(-1);
        }

        return splits.Select((s, i) => s with { Window = new TimeWindow(from[i], to[i]) }).ToList();
    }

    /// <summary>Index of the first part whose window holds <paramref name="when"/>, or null if none does.</summary>
    public static int? Assign(IReadOnlyList<ProjectSplit> splits, DateTime when)
    {
        for (var i = 0; i < splits.Count; i++)
        {
            if (splits[i].Window.Contains(when)) return i;
        }
        return null;
    }
}

/// <summary>
/// "Split by breaks": groups clips into recording sessions wherever nothing was recorded for a while.
/// Three locations in a day usually show up as three groups, with travel time between them.
/// </summary>
public static class SessionSplitter
{
    public static readonly TimeSpan DefaultBreak = TimeSpan.FromMinutes(60);

    /// <summary>
    /// One window per session, from the first clip to the last clip in it. Returns an empty list when the
    /// clips span more than one day (a time of day can't tell days apart) or there are no clips.
    /// </summary>
    public static IReadOnlyList<TimeWindow> Suggest(IEnumerable<DateTime> clipTimes, TimeSpan? minBreak = null)
    {
        var gap = minBreak ?? DefaultBreak;
        var times = clipTimes.Order().ToList();
        if (times.Count == 0) return [];
        if (times.Select(t => t.Date).Distinct().Count() > 1) return [];

        var windows = new List<TimeWindow>();
        var start = times[0];
        var prev = times[0];
        foreach (var t in times.Skip(1))
        {
            if (t - prev >= gap)
            {
                windows.Add(Window(start, prev));
                start = t;
            }
            prev = t;
        }
        windows.Add(Window(start, prev));
        return windows;
    }

    private static TimeWindow Window(DateTime first, DateTime last) =>
        new(new TimeOnly(first.Hour, first.Minute), new TimeOnly(last.Hour, last.Minute));
}
