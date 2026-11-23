using System.Globalization;

namespace ShootDrop.Core.Media;

public enum DateRuleKind
{
    Today,
    Yesterday,
    LastDays,
    Range,
}

/// <summary>
/// Which recording dates to copy. Stored relative ("yesterday"), so a saved setup
/// still means "yesterday" when it runs next week.
/// </summary>
public sealed record DateRule(DateRuleKind Kind, int Days = 1, DateOnly? From = null, DateOnly? To = null)
{
    public static DateRule Today { get; } = new(DateRuleKind.Today);
    public static DateRule Yesterday { get; } = new(DateRuleKind.Yesterday);

    public static DateRule LastDays(int days) =>
        days < 1 ? throw new ArgumentOutOfRangeException(nameof(days)) : new(DateRuleKind.LastDays, days);

    public static DateRule Between(DateOnly from, DateOnly to) =>
        to < from ? new(DateRuleKind.Range, From: to, To: from) : new(DateRuleKind.Range, From: from, To: to);

    public (DateOnly From, DateOnly To) Resolve(DateOnly today) => Kind switch
    {
        DateRuleKind.Today => (today, today),
        DateRuleKind.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
        DateRuleKind.LastDays => (today.AddDays(-(Days - 1)), today),
        DateRuleKind.Range => (From ?? throw new InvalidOperationException("Range needs From"),
                               To ?? throw new InvalidOperationException("Range needs To")),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public bool Contains(DateOnly date, DateOnly today)
    {
        var (from, to) = Resolve(today);
        return date >= from && date <= to;
    }

    /// <summary>"Today, Mon 23 Nov", "Sat 21 to Mon 23 Nov".</summary>
    public string Describe(DateOnly today)
    {
        var (from, to) = Resolve(today);
        static string D(DateOnly d) => d.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        return Kind switch
        {
            DateRuleKind.Today => $"Today, {D(from)}",
            DateRuleKind.Yesterday => $"Yesterday, {D(from)}",
            _ when from == to => D(from),
            _ => $"{D(from)} to {D(to)}",
        };
    }

    /// <summary>Parses CLI values: today, yesterday, last3, 2026-11-22, 2026-11-21..2026-11-22.</summary>
    public static DateRule Parse(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t == "today") return Today;
        if (t == "yesterday") return Yesterday;
        if (t.StartsWith("last", StringComparison.Ordinal) && int.TryParse(t.AsSpan(4), out var n)) return LastDays(n);

        var parts = t.Split("..", StringSplitOptions.TrimEntries);
        if (parts.Length is 1 or 2
            && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a))
        {
            if (parts.Length == 1) return Between(a, a);
            if (DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
                return Between(a, b);
        }

        throw new FormatException($"Unknown date '{text}'. Use today, yesterday, last3, 2026-11-22 or 2026-11-21..2026-11-22.");
    }
}
