using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;

namespace ShootDrop.Core.Media;

/// <summary>A clip on the timeline: which card it is on, and the file (its time is when recording stopped).</summary>
public sealed record TimelineClip(string CardName, SourceFile File)
{
    public DateTime Time => File.Modified;
}

/// <summary>A group of clips recorded close together, e.g. one location. Clips are in time order.</summary>
public sealed record ClipSession(IReadOnlyList<TimelineClip> Clips)
{
    public DateTime First => Clips[0].Time;
    public DateTime Last => Clips[^1].Time;
    public long TotalBytes => Clips.Sum(c => c.File.Size);
    public IReadOnlyList<string> CardNames => Clips.Select(c => c.CardName).Distinct().ToList();
    public TimeWindow Window => new(Minute(First), Minute(Last));

    private static TimeOnly Minute(DateTime t) => new(t.Hour, t.Minute);
}

/// <summary>
/// The day's clips grouped into sessions wherever recording paused for a while, so the user can see
/// "9 am – 1 pm, 2:30 – 5:45 pm, 8 – 11 pm" and give each group its own project. The user can merge
/// two groups or start a new group at any clip. Only for a single day: a time of day can't tell days apart.
/// </summary>
public static class ClipTimeline
{
    /// <summary>Sessions for these cards' clips, or an empty list when there are none or they span several days.</summary>
    public static IReadOnlyList<ClipSession> Build(IEnumerable<CardFiles> cards, TimeSpan? minBreak = null)
    {
        var gap = minBreak ?? SessionSplitter.DefaultBreak;
        var clips = cards.SelectMany(c => c.Files.Select(f => new TimelineClip(c.Card.Name, f)))
            .OrderBy(c => c.Time).ThenBy(c => c.File.Path, StringComparer.Ordinal).ToList();
        if (clips.Count == 0 || !IsSingleDay(clips)) return [];

        var sessions = new List<ClipSession>();
        var current = new List<TimelineClip> { clips[0] };
        foreach (var clip in clips.Skip(1))
        {
            if (clip.Time - current[^1].Time > gap)
            {
                sessions.Add(new ClipSession(current));
                current = [];
            }
            current.Add(clip);
        }
        sessions.Add(new ClipSession(current));
        return sessions;
    }

    public static bool IsSingleDay(IEnumerable<TimelineClip> clips) => clips.Select(c => c.Time.Date).Distinct().Count() <= 1;

    /// <summary>Joins session <paramref name="index"/> with the one after it.</summary>
    public static IReadOnlyList<ClipSession> MergeWithNext(IReadOnlyList<ClipSession> sessions, int index)
    {
        if (index < 0 || index >= sessions.Count - 1) return sessions;
        var list = sessions.ToList();
        list[index] = new ClipSession([.. sessions[index].Clips, .. sessions[index + 1].Clips]);
        list.RemoveAt(index + 1);
        return list;
    }

    /// <summary>
    /// True when a new session can start at clip <paramref name="clipIndex"/> of session <paramref name="index"/>:
    /// not the first clip, and not in the same minute as the clip before it (times are compared to the minute).
    /// </summary>
    public static bool CanSplitBefore(IReadOnlyList<ClipSession> sessions, int index, int clipIndex)
    {
        if (index < 0 || index >= sessions.Count) return false;
        var clips = sessions[index].Clips;
        if (clipIndex <= 0 || clipIndex >= clips.Count) return false;
        var a = clips[clipIndex - 1].Time;
        var b = clips[clipIndex].Time;
        return new DateTime(a.Year, a.Month, a.Day, a.Hour, a.Minute, 0) != new DateTime(b.Year, b.Month, b.Day, b.Hour, b.Minute, 0);
    }

    /// <summary>Starts a new session at clip <paramref name="clipIndex"/> of session <paramref name="index"/>.</summary>
    public static IReadOnlyList<ClipSession> SplitBefore(IReadOnlyList<ClipSession> sessions, int index, int clipIndex)
    {
        if (!CanSplitBefore(sessions, index, clipIndex)) return sessions;
        var clips = sessions[index].Clips;
        var list = sessions.ToList();
        list[index] = new ClipSession(clips.Take(clipIndex).ToList());
        list.Insert(index + 1, new ClipSession(clips.Skip(clipIndex).ToList()));
        return list;
    }

    /// <summary>
    /// The copy split for these sessions: each session's clips go to its project name.
    /// Sessions sharing a name share a project.
    /// </summary>
    public static IReadOnlyList<ProjectSplit> ToSplits(IReadOnlyList<ClipSession> sessions, IReadOnlyList<string> names)
    {
        if (names.Count != sessions.Count) throw new ArgumentException("One name per session.", nameof(names));
        return sessions.Select((s, i) => new ProjectSplit(names[i].Trim(), s.Window)).ToList();
    }

    /// <summary>A stable fingerprint of the clips, so the app can keep the user's grouping while nothing changed.</summary>
    public static string Signature(IReadOnlyList<ClipSession> sessions) =>
        string.Join("|", sessions.SelectMany(s => s.Clips).Select(c => c.File.Path).Order(StringComparer.Ordinal));
}
