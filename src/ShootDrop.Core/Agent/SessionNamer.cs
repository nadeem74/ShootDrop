using System.Text.RegularExpressions;
using ShootDrop.Core.Autopilot;

namespace ShootDrop.Core.Agent;

/// <summary>A name suggested for one session, and where it came from (grounding, ADR-008).</summary>
public sealed record NameSuggestion(int Session, string Name, string Source);

/// <summary>What a naming run decided, with a readable trace of why (FR-27).</summary>
public sealed record NamingResult(IReadOnlyList<NameSuggestion> Names, IReadOnlyList<string> Trace)
{
    public string? NameFor(int session) => Names.FirstOrDefault(n => n.Session == session)?.Name;
}

/// <summary>The facts a name may come from. Anything else is made up and is refused.</summary>
public sealed record NamingFacts(string Note, IReadOnlyList<string> PastProjects, IReadOnlyList<AutoSession> Sessions)
{
    public static NamingFacts For(AutoProposal proposal, string note, string projectsRoot) =>
        new(note ?? "", PastProjectsIn(projectsRoot), proposal.Sessions);

    /// <summary>Project folders already in the projects folder, newest first (at most 50).</summary>
    public static IReadOnlyList<string> PastProjectsIn(string projectsRoot)
    {
        try
        {
            return Directory.Exists(projectsRoot)
                ? new DirectoryInfo(projectsRoot).GetDirectories()
                    .Where(d => !d.Name.StartsWith('.'))
                    .OrderByDescending(d => d.LastWriteTimeUtc).Take(50).Select(d => d.Name).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>
/// Grounding: a suggested name is accepted only if it can be traced to a fact. The check is code, not a prompt,
/// so a model can't talk its way past it.
/// </summary>
public static class Grounding
{
    /// <summary>The source of <paramref name="name"/> if it is grounded, else null.</summary>
    public static string? SourceOf(string name, NamingFacts facts)
    {
        var n = name.Trim();
        if (n.Length == 0) return null;
        if (facts.Sessions.Any(s => s.DefaultName.Equals(n, StringComparison.OrdinalIgnoreCase)))
            return "date and time of the first clip";
        if (facts.Note.Length > 0 && facts.Note.Contains(n, StringComparison.OrdinalIgnoreCase))
            return "your note: “" + facts.Note.Trim() + "”";
        if (facts.PastProjects.Any(p => p.Equals(n, StringComparison.OrdinalIgnoreCase)))
            return "an existing project with that name";
        return null;
    }

    /// <summary>Keeps grounded names, replaces the rest by the date name, and explains each choice in the trace.</summary>
    public static NamingResult Enforce(IEnumerable<NameSuggestion> proposed, NamingFacts facts, List<string> trace)
    {
        var accepted = new List<NameSuggestion>();
        foreach (var p in proposed.Where(p => p.Session >= 0 && p.Session < facts.Sessions.Count))
        {
            var label = $"Session {p.Session + 1} ({facts.Sessions[p.Session].First:HH:mm})";
            if (SourceOf(p.Name, facts) is { } source)
            {
                accepted.Add(p with { Name = p.Name.Trim(), Source = source });
                trace.Add($"{label} → “{p.Name.Trim()}”, from {source}.");
            }
            else
            {
                trace.Add($"{label}: refused “{p.Name}”, it isn't in your note or your projects. Kept “{facts.Sessions[p.Session].DefaultName}”.");
            }
        }
        return new NamingResult(accepted, trace);
    }
}

/// <summary>
/// Rules first (AI.md): names sessions from the note without any model. "morning Sinhagad Fort, evening Cafe review"
/// with two sessions → Sinhagad Fort, Cafe review. One name → every session goes into that one project.
/// </summary>
public static partial class SessionNamer
{
    [GeneratedRegex(@"\s*(?:,|;|\n|\bthen\b|\band then\b|\bafter that\b|\bphir\b|\baur\b|\baur phir\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Separators();

    [GeneratedRegex(@"^(?:in the |the |at |at the )?(?:early |late )?(?<word>morning|afternoon|evening|night|noon|subah|dopahar|shaam|sham|raat)(?<sep>\s*[:\-–]\s*|\s+(?:at|me|mein)\s+|\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex TimeWords();

    /// <summary>
    /// "morning Sinhagad Fort" → "Sinhagad Fort", but "Night walk" stays "Night walk": a time word is only a prefix when
    /// it is followed by ':' / '-' / "at" / "mein", or written in lower case before a capitalised name.
    /// </summary>
    private static string WithoutTimeWord(string piece)
    {
        var m = TimeWords().Match(piece);
        if (!m.Success || m.Length >= piece.Length) return piece;
        var explicitSep = m.Groups["sep"].Value.Trim().Length > 0;
        var lowerThenName = char.IsLower(m.Groups["word"].Value[0]) && char.IsUpper(piece[m.Length]);
        return explicitSep || lowerThenName ? piece[m.Length..] : piece;
    }

    /// <summary>The note split into project names, in order.</summary>
    public static IReadOnlyList<string> Pieces(string note) =>
        Separators().Split(note ?? "")
            .Select(p => WithoutTimeWord(p.Trim()).Trim(' ', '.', '-', '–'))
            .Where(p => p.Length > 0)
            .ToList();

    public static NamingResult FromNote(NamingFacts facts)
    {
        var trace = new List<string>();
        var pieces = Pieces(facts.Note);
        var count = facts.Sessions.Count;
        if (pieces.Count == 0)
        {
            trace.Add("No note, so every session keeps its date name.");
            return new NamingResult([], trace);
        }
        IEnumerable<NameSuggestion> proposed;
        if (pieces.Count == 1)
        {
            trace.Add($"Your note names one project, so all {count} session(s) go into it.");
            proposed = Enumerable.Range(0, count).Select(i => new NameSuggestion(i, pieces[0], ""));
        }
        else if (pieces.Count == count)
        {
            trace.Add($"Your note names {count} projects for {count} sessions: matched in order.");
            proposed = pieces.Select((p, i) => new NameSuggestion(i, p, ""));
        }
        else
        {
            trace.Add($"Your note names {pieces.Count} projects but there are {count} sessions; rules can't match them, so date names stay.");
            return new NamingResult([], trace);
        }
        return Grounding.Enforce(proposed, facts, trace);
    }
}
