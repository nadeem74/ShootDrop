using System.Globalization;
using System.Text.RegularExpressions;
using ShootDrop.Core.Media;

namespace ShootDrop.Core.SmartBox;

/// <summary>
/// Time windows and per-project splits for the smart box:
/// "copy yesterday's footage till 1 pm into Project A, 1 pm to 6 pm into B and after 6 pm into C".
/// Also "1-6pm", "between 2 and 5 pm", "after 18:30", "1 baje tak".
/// </summary>
public static partial class RuleParser
{
    private sealed record TimeMatch(int Index, int Length, TimeWindow Window);

    /// <summary>
    /// Finds the time-split parts of <paramref name="text"/>. Returns them (chained, in order) and a copy of
    /// the text with those parts blanked out, so the rest of the parser doesn't read "6 pm" as a date or
    /// "project B" as the main project.
    /// </summary>
    internal static (IReadOnlyList<ProjectSplit>? Splits, string Rest) ParseSplits(string text)
    {
        var lower = text.ToLowerInvariant();
        var times = FindTimes(lower);
        if (times.Count == 0) return (null, text);

        // Mask times so "and" inside "between 1 and 6 pm" isn't taken as a separator.
        var masked = lower.ToCharArray();
        foreach (var t in times)
            for (var i = t.Index; i < t.Index + t.Length; i++) masked[i] = '§';
        var maskedText = new string(masked);

        var blank = text.ToCharArray();
        var splits = new List<ProjectSplit>();
        foreach (var (start, end) in Segments(maskedText))
        {
            var inSegment = times.Where(t => t.Index >= start && t.Index < end).ToList();
            if (inSegment.Count == 0) continue;
            var time = inSegment[0];

            // Project after the time ("till 1 pm into Project A"), or before it ("for vlog, after 6 pm").
            var after = text[(time.Index + time.Length)..end];
            var before = text[start..time.Index];
            var (name, nameStart, nameEnd) = FindProject(after, time.Index + time.Length);
            if (name is null) (name, nameStart, nameEnd) = FindProject(before, start);

            splits.Add(new ProjectSplit(name, time.Window));
            for (var i = time.Index; i < time.Index + time.Length; i++) blank[i] = ' ';
            if (name is not null)
                for (var i = nameStart; i < nameEnd; i++) blank[i] = ' ';
        }

        if (splits.Count == 0) return (null, text);
        return (ProjectSplits.Chain(splits), new string(blank));
    }

    /// <summary>Segment boundaries: commas, semicolons, "and", "then", "also".</summary>
    private static IEnumerable<(int Start, int End)> Segments(string masked)
    {
        var start = 0;
        foreach (Match m in SeparatorRegex().Matches(masked))
        {
            yield return (start, m.Index);
            start = m.Index + m.Length;
        }
        yield return (start, masked.Length);
    }

    private static (string? Name, int Start, int End) FindProject(string part, int offset)
    {
        var m = SplitProjectRegex().Match(part);
        if (!m.Success) return (null, 0, 0);
        var g = m.Groups["name"];
        var name = g.Value.Trim().Trim('"', '\'', '“', '”', '.').Trim();
        name = FolderWordRegex().Replace(name, "").Trim();
        if (name.Length == 0) return (null, 0, 0);
        return (name, offset + m.Index, offset + m.Index + m.Length);
    }

    private static List<TimeMatch> FindTimes(string lower)
    {
        var found = new List<TimeMatch>();
        bool Free(Match m) => !found.Any(f => m.Index < f.Index + f.Length && f.Index < m.Index + m.Length);

        foreach (Match m in TimeRangeRegex().Matches(lower))
        {
            if (!Free(m)) continue;
            var hasMarker = m.Groups["ap1"].Success || m.Groups["ap2"].Success || m.Groups["m1"].Success
                            || m.Groups["m2"].Success || m.Groups["baje"].Success || m.Groups["lead"].Success;
            if (!hasMarker) continue; // "21-22" alone is more likely dates than times
            var ap2 = Meridiem(m.Groups["ap2"]);
            var ap1 = Meridiem(m.Groups["ap1"]);
            var h1 = Hour(m.Groups["h1"]);
            var h2 = Hour(m.Groups["h2"]);
            if (h1 is null || h2 is null) continue;
            var to = ToTime(h2.Value, Minute(m.Groups["m2"]), ap2);
            // "1-6pm": the first time borrows "pm"; "11-1pm" becomes 11 am to 1 pm.
            var from = ToTime(h1.Value, Minute(m.Groups["m1"]), ap1 ?? ap2);
            if (to is null || from is null) continue;
            if (ap1 is null && ap2 is not null && from > to) from = ToTime(h1.Value, Minute(m.Groups["m1"]), ap2 == 'p' ? 'a' : 'p');
            found.Add(new TimeMatch(m.Index, m.Length, new TimeWindow(from, to)));
        }

        foreach (Match m in TillRegex().Matches(lower))
        {
            if (!Free(m)) continue;
            if (Hour(m.Groups["h"]) is not { } h || ToTime(h, Minute(m.Groups["m"]), Meridiem(m.Groups["ap"])) is not { } t) continue;
            found.Add(new TimeMatch(m.Index, m.Length, new TimeWindow(null, t)));
        }

        foreach (Match m in TakRegex().Matches(lower))
        {
            if (!Free(m)) continue;
            if (Hour(m.Groups["h"]) is not { } h || ToTime(h, Minute(m.Groups["m"]), Meridiem(m.Groups["ap"])) is not { } t) continue;
            found.Add(new TimeMatch(m.Index, m.Length, new TimeWindow(null, t)));
        }

        foreach (Match m in AfterRegex().Matches(lower))
        {
            if (!Free(m)) continue;
            if (Hour(m.Groups["h"]) is not { } h || ToTime(h, Minute(m.Groups["m"]), Meridiem(m.Groups["ap"])) is not { } t) continue;
            found.Add(new TimeMatch(m.Index, m.Length, new TimeWindow(t, null)));
        }

        found.Sort((a, b) => a.Index.CompareTo(b.Index));
        return found;
    }

    private static int? Hour(Group g)
    {
        if (!g.Success) return null;
        if (g.Value is "noon") return 12;
        if (g.Value is "midnight") return 0;
        return int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h <= 24 ? h : null;
    }

    private static int Minute(Group g) =>
        g.Success && int.TryParse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m < 60 ? m : 0;

    private static char? Meridiem(Group g) => g.Success ? g.Value.TrimStart()[0] : null;

    /// <summary>
    /// Turns 1-12 plus am/pm into a time. Without am/pm, footage times are read the way people say them:
    /// 7-11 is morning, 12 is noon, 1-6 is afternoon; 13-23 is the 24-hour clock.
    /// </summary>
    private static TimeOnly? ToTime(int hour, int minute, char? meridiem)
    {
        if (hour == 24) hour = 0;
        if (hour > 12) return meridiem is null ? new TimeOnly(hour, minute) : null;
        var h = meridiem switch
        {
            'p' => hour == 12 ? 12 : hour + 12,
            'a' => hour == 12 ? 0 : hour,
            _ => hour is >= 1 and <= 6 ? hour + 12 : hour,
        };
        return new TimeOnly(h, minute);
    }

    [GeneratedRegex(@"(?<lead>\bfrom\s+|\bbetween\s+)?\b(?<h1>\d{1,2}|noon)(?!\d)(?:[:.](?<m1>\d{2}))?\s*(?<ap1>a\.?\s?m\.?|p\.?\s?m\.?)?\s*(?:-|–|\bto\b|\btill\b|\buntil\b|\band\b)\s*(?<h2>\d{1,2}|noon|midnight)(?!\d)(?:[:.](?<m2>\d{2}))?\s*(?<ap2>a\.?\s?m\.?|p\.?\s?m\.?)?(?<baje>\s*baje)?(?!\s*(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec|st\b|nd\b|rd\b|th\b|days?\b))")]
    private static partial Regex TimeRangeRegex();

    [GeneratedRegex(@"\b(?:till|until|untill|upto|up\s+to|before|by)\s+(?<h>\d{1,2}|noon|midnight)(?!\d)(?:[:.](?<m>\d{2}))?\s*(?<ap>a\.?\s?m\.?|p\.?\s?m\.?)?(?:\s*baje)?(?!\s*(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec|days?\b))")]
    private static partial Regex TillRegex();

    [GeneratedRegex(@"\b(?<h>\d{1,2})(?!\d)(?:[:.](?<m>\d{2}))?\s*(?<ap>a\.?\s?m\.?|p\.?\s?m\.?)?\s*(?:baje\s*)?tak\b")]
    private static partial Regex TakRegex();

    [GeneratedRegex(@"\b(?:after|from|since)\s+(?<h>\d{1,2}|noon)(?!\d)(?:[:.](?<m>\d{2}))?\s*(?<ap>a\.?\s?m\.?|p\.?\s?m\.?)?(?:\s*baje)?(?!\s*(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec|days?\b))")]
    private static partial Regex AfterRegex();

    [GeneratedRegex(@"\s*(?:[,;]|\band\b|\bthen\b|\balso\b|\baur\b)\s*")]
    private static partial Regex SeparatorRegex();

    // "into Project A", "in the C folder", "for Cafe review", "project A mein" (Hinglish).
    [GeneratedRegex(@"(?:\b(?:in|into|inside|for|to|under)\s+(?:the\s+)?(?:project\s+|folder\s+)?|\bproject\s+)(?<name>[^,;§]+?)(?:\s+(?:mein|me|main))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SplitProjectRegex();

    [GeneratedRegex(@"\s+(?:folder|project)$", RegexOptions.IgnoreCase)]
    private static partial Regex FolderWordRegex();
}
