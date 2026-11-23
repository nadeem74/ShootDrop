using System.Globalization;
using System.Text.RegularExpressions;
using ShootDrop.Core.Media;

namespace ShootDrop.Core.SmartBox;

/// <summary>A card the parser can name, e.g. key "dji-osmo-pocket" with aliases "pocket", "osmo".</summary>
public sealed record KnownCard(string Key, IReadOnlyList<string> Aliases);

/// <summary>
/// What the smart box understood. Null fields mean "not mentioned, keep the current choice".
/// <see cref="CardKeys"/> null + <see cref="AllCards"/> true means "all cards".
/// </summary>
public sealed record ParsedRequest(
    DateRule? Date,
    bool AllCards,
    IReadOnlyList<string>? CardKeys,
    IReadOnlySet<FileKind>? Kinds,
    string? ProjectName)
{
    public bool UnderstoodAnything => Date is not null || AllCards || CardKeys is not null || Kinds is not null || ProjectName is not null;
}

/// <summary>
/// v0.1 smart box (ADR-005): fast, offline, no model. Understands English and a little Hinglish.
/// Examples: "all cards, yesterday for Sinhagad trip", "Pocket only, last 3 days",
/// "audio only today", "kal ka sab footage with photos", "21-22 Nov".
/// </summary>
public static partial class RuleParser
{
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["do"] = 2, ["teen"] = 3, ["char"] = 4, ["paanch"] = 5,
    };

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    public static ParsedRequest Parse(string text, DateOnly today, IReadOnlyList<KnownCard> cards)
    {
        var s = text.Trim();
        var lower = s.ToLowerInvariant();

        // Project name first, so words inside it ("for Kal Ho Na Ho shoot") don't count as filters.
        string? project = null;
        var pm = ProjectRegex().Match(s);
        if (pm.Success)
        {
            project = pm.Groups["name"].Value.Trim().Trim('"', '\'', '“', '”').Trim();
            if (project.Length == 0) project = null;
            lower = lower[..pm.Index];
        }

        var date = ParseDate(lower, today);

        var all = AllRegex().IsMatch(lower);
        List<string>? keys = null;
        if (!all)
        {
            foreach (var card in cards)
            {
                if (card.Aliases.Any(a => Regex.IsMatch(lower, $@"\b{Regex.Escape(a.ToLowerInvariant())}\b")))
                {
                    (keys ??= []).Add(card.Key);
                }
            }
        }

        HashSet<FileKind>? kinds = null;
        if (OnlyAudioRegex().IsMatch(lower)) kinds = [FileKind.Audio];
        else if (OnlyVideoRegex().IsMatch(lower)) kinds = [FileKind.Video];
        if (PhotoRegex().IsMatch(lower)) (kinds ??= [FileKind.Video, FileKind.Audio]).Add(FileKind.Photo);
        if (ProxyRegex().IsMatch(lower)) (kinds ??= [FileKind.Video, FileKind.Audio]).Add(FileKind.Proxy);

        return new ParsedRequest(date, all, keys, kinds, project);
    }

    private static DateRule? ParseDate(string s, DateOnly today)
    {
        // "kal" means yesterday or tomorrow in Hindi; for copying footage only the past makes sense.
        if (YesterdayRegex().IsMatch(s)) return DateRule.Yesterday;

        var last = LastDaysRegex().Match(s);
        if (last.Success)
        {
            var n = last.Groups["n"].Value;
            var days = int.TryParse(n, out var d) ? d : NumberWords.GetValueOrDefault(n, 0);
            if (days >= 1) return DateRule.LastDays(days);
        }

        if (TodayRegex().IsMatch(s)) return DateRule.Today;

        var range = DayRangeRegex().Match(s);
        if (range.Success && MonthIndex(range.Groups["mon"].Value) is { } m1)
        {
            var a = ToDate(int.Parse(range.Groups["d1"].Value, CultureInfo.InvariantCulture), m1, today);
            var b = ToDate(int.Parse(range.Groups["d2"].Value, CultureInfo.InvariantCulture), m1, today);
            if (a is not null && b is not null) return DateRule.Between(a.Value, b.Value);
        }

        var single = DayMonthRegex().Match(s);
        if (!single.Success) single = MonthDayRegex().Match(s);
        if (single.Success && MonthIndex(single.Groups["mon"].Value) is { } m2)
        {
            var day = ToDate(int.Parse(single.Groups["d"].Value, CultureInfo.InvariantCulture), m2, today);
            if (day is not null) return DateRule.Between(day.Value, day.Value);
        }

        return null;
    }

    private static int? MonthIndex(string word)
    {
        var i = Array.IndexOf(Months, word[..3].ToLowerInvariant());
        return i < 0 ? null : i + 1;
    }

    /// <summary>A day without a year means the most recent one that isn't in the future.</summary>
    private static DateOnly? ToDate(int day, int month, DateOnly today)
    {
        if (day < 1 || day > DateTime.DaysInMonth(today.Year, month)) return null;
        var d = new DateOnly(today.Year, month, day);
        return d > today ? d.AddYears(-1) : d;
    }

    [GeneratedRegex(@"\b(?:for|project|called|named)\s+(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"\b(?:all|every|sab|saare|sare)\b")]
    private static partial Regex AllRegex();

    [GeneratedRegex(@"\b(?:yesterday|kal)\b")]
    private static partial Regex YesterdayRegex();

    [GeneratedRegex(@"\b(?:today|aaj|tonight)\b")]
    private static partial Regex TodayRegex();

    [GeneratedRegex(@"\b(?:last|past|pichle)\s+(?<n>\d+|two|three|four|five|six|seven|do|teen|char|paanch)\s+(?:days?|din)\b")]
    private static partial Regex LastDaysRegex();

    [GeneratedRegex(@"\b(?<d1>\d{1,2})\s*(?:-|to|–)\s*(?<d2>\d{1,2})\s*(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b")]
    private static partial Regex DayRangeRegex();

    [GeneratedRegex(@"\b(?<d>\d{1,2})(?:st|nd|rd|th)?\s*(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b")]
    private static partial Regex DayMonthRegex();

    [GeneratedRegex(@"\b(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\s+(?<d>\d{1,2})(?:st|nd|rd|th)?\b")]
    private static partial Regex MonthDayRegex();

    [GeneratedRegex(@"\b(?:audio only|only audio|just audio|sirf audio)\b")]
    private static partial Regex OnlyAudioRegex();

    [GeneratedRegex(@"\b(?:video only|only video|just video|sirf video)\b")]
    private static partial Regex OnlyVideoRegex();

    [GeneratedRegex(@"\b(?:photos?|pictures?|images?|pics)\b")]
    private static partial Regex PhotoRegex();

    [GeneratedRegex(@"\b(?:proxy|proxies|lrf)\b")]
    private static partial Regex ProxyRegex();
}
