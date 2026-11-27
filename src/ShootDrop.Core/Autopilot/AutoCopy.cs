using System.Globalization;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Autopilot;

/// <summary>One recording session the autopilot found, with the name it will get unless the person changes it.</summary>
public sealed record AutoSession(DateOnly Day, ClipSession Session, string DefaultName)
{
    public DateTime First => Session.First;
    public DateTime Last => Session.Last;
    public int ClipCount => Session.Clips.Count;
    public long TotalBytes => Session.TotalBytes;
}

/// <summary>What the autopilot found on the plugged-in cards: the new sessions, oldest first.</summary>
public sealed record AutoProposal(IReadOnlyList<DetectedCard> Cards, IReadOnlyList<AutoSession> Sessions, IReadOnlyList<CardFiles> NewFiles)
{
    public long TotalBytes => Sessions.Sum(s => s.TotalBytes);
    public bool HasWork => Sessions.Count > 0;

    /// <summary>Cards with new clips but no folder yet (unknown device). The person must choose one first.</summary>
    public IReadOnlyList<DetectedCard> UnknownCards => NewFiles.Where(f => !f.Card.IsKnown).Select(f => f.Card).ToList();
}

/// <summary>
/// FR-26, ADR-008. The autopilot: cards in → what's new → sessions with ready names → one Start → copy.
/// Plain rules, no AI. Used by the app (card inserted, menu bar) and by <c>shootdrop auto</c>, so all behave the same.
/// </summary>
/// <remarks>
/// "New" means: recorded after the last autopilot copy from that card. A card it has never copied from offers its
/// most recent recording day only, so an old card full of footage doesn't propose months of sessions.
/// Running it twice copies nothing twice: names come from clip times, so the same session gets the same folder and
/// the engine skips files that are already there and identical (idempotent).
/// </remarks>
public sealed class AutoCopy(ShootDropService service)
{
    /// <summary>Date and time of the first clip, e.g. "2026-11-27 0915". No ':' because it's unsafe in folder names.</summary>
    public static string NameFor(DateTime first) => first.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);

    public static IReadOnlySet<FileKind> KindsFor(AppSettings settings) => settings.CurrentSetup.Kinds.ToHashSet();

    public AutoProposal Propose(AppSettings settings, IReadOnlyList<DetectedCard>? cards = null)
    {
        cards ??= service.DetectCards(settings);
        var scanner = new CardScanner(settings.BuildFileTypes());
        var kinds = KindsFor(settings);

        var newFiles = new List<CardFiles>();
        foreach (var card in cards)
        {
            var all = scanner.Scan(card.Volume.Path, kinds, DateOnly.MinValue, DateOnly.MaxValue);
            if (all.Count == 0) continue;
            IReadOnlyList<SourceFile> fresh;
            if (settings.AutoCopiedUpTo.TryGetValue(card.CardId, out var mark))
            {
                fresh = all.Where(f => f.Modified > mark).ToList();
            }
            else
            {
                var newest = all.Max(f => f.Modified.Date);
                fresh = all.Where(f => f.Modified.Date == newest).ToList();
            }
            if (fresh.Count > 0) newFiles.Add(new CardFiles(card, fresh));
        }

        var sessions = new List<AutoSession>();
        var days = newFiles.SelectMany(c => c.Files).Select(f => DateOnly.FromDateTime(f.Modified)).Distinct().Order();
        foreach (var day in days)
        {
            var ofDay = newFiles
                .Select(c => c with { Files = c.Files.Where(f => DateOnly.FromDateTime(f.Modified) == day).ToList() })
                .Where(c => c.Files.Count > 0);
            foreach (var s in ClipTimeline.Build(ofDay, settings.TimelineBreak))
                sessions.Add(new AutoSession(day, s, NameFor(s.First)));
        }
        return new AutoProposal(cards, sessions, newFiles);
    }

    /// <summary>
    /// The copy for the sessions the person kept, each into its own project (<paramref name="names"/> by session index;
    /// blank → the default name). Sessions given the same name share one project.
    /// </summary>
    public CopyPreview Preview(AutoProposal proposal, IReadOnlyList<string?> names, IReadOnlySet<int> skipped, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectsRoot))
            throw new InvalidOperationException("Choose where projects go first.");
        var chosen = proposal.Sessions.Select((s, i) => (s, i)).Where(x => !skipped.Contains(x.i)).ToList();
        if (chosen.Count == 0) throw new InvalidOperationException("Nothing to copy: every session is unticked.");

        string NameOf(int i) => ShootDropService.SanitizeProjectName(
            i < names.Count && !string.IsNullOrWhiteSpace(names[i]) ? names[i]!.Trim() : proposal.Sessions[i].DefaultName);

        var parts = new List<(TimeWindow Window, CopyPlan Plan)>();
        foreach (var group in chosen.GroupBy(x => NameOf(x.i), StringComparer.OrdinalIgnoreCase))
        {
            var clips = group.SelectMany(x => x.s.Session.Clips).ToList();
            var cardFiles = clips.GroupBy(c => c.CardName)
                .Select(g => new CardFiles(proposal.Cards.First(c => c.Name == g.Key), g.Select(c => c.File).ToList()))
                .ToList();
            var first = group.First().s;
            var window = new TimeWindow(first.Session.Window.From, group.Last().s.Session.Window.To);
            parts.Add((window, CopyPlanner.Build(Path.Combine(settings.ProjectsRoot, group.Key), cardFiles, settings.Routing)));
        }

        // The request keeps the current setup's dates and cards: the plan above is already exact, and a copy
        // remembers its request as "last used", which must stay a rule like "today", not a fixed past date.
        var current = settings.CurrentSetup;
        var request = new CopyRequest(NameOf(chosen[0].i), current.Date, KindsFor(settings), current.CardKeys);
        return new CopyPreview(request, proposal.NewFiles, CopyPlan.Combine(parts, []));
    }

    /// <summary>
    /// After a copy: remember, per card, the newest clip that is now safely in a project, so the next card
    /// insert only offers what's newer. Failed files don't count, so they're offered again.
    /// </summary>
    public static void Remember(AppSettings settings, IEnumerable<CopyFileResult> results)
    {
        foreach (var g in results.Where(r => r.Outcome != CopyOutcome.Failed).GroupBy(r => r.Item.CardName))
        {
            var newest = g.Max(r => r.Item.Source.Modified);
            if (!settings.AutoCopiedUpTo.TryGetValue(g.Key, out var old) || newest > old)
                settings.AutoCopiedUpTo[g.Key] = newest;
        }
    }

    /// <summary>One line per session for a terminal or notification: "09:15 – 12:40  24 clips · 38 GB → 2026-11-27 0915".</summary>
    public static string Describe(AutoSession s) =>
        $"{s.Day:ddd d MMM}  {s.First:HH:mm} – {s.Last:HH:mm}  {s.ClipCount} clips · {StorageCheck.Bytes(s.TotalBytes)}";
}
