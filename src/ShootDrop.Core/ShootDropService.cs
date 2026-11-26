using System.Globalization;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core;

/// <summary>
/// What the user asked for: one project, one date rule, some file types, some cards.
/// With <see cref="Splits"/>, clips are spread over several projects by the time they were recorded.
/// </summary>
public sealed record CopyRequest(
    string ProjectName,
    DateRule Date,
    IReadOnlySet<FileKind> Kinds,
    IReadOnlyList<string>? CardKeys,
    IReadOnlyList<ProjectSplit>? Splits = null)
{
    public static CopyRequest FromSetup(Setup setup, string projectName) =>
        new(projectName, setup.Date, setup.Kinds.ToHashSet(), setup.CardKeys);

    public Setup ToSetup(string name) => new(name, CardKeys, Date, Kinds.Order().ToList());
}

public sealed record CopyPreview(CopyRequest Request, IReadOnlyList<CardFiles> Cards, CopyPlan Plan);

/// <summary>The rest of an unfinished copy. <see cref="MissingCards"/> must be plugged in to copy every file.</summary>
public sealed record ResumePreview(CopyPreview Preview, int AlreadyDone, int MissingSources, IReadOnlyList<string> MissingCards);

/// <param name="ReportPath">The report of the first project; <see cref="ReportPaths"/> has one per project.</param>
public sealed record CopyRunResult(CopyPlan Plan, IReadOnlyList<CopyFileResult> Files, string ReportPath, IReadOnlyList<string>? ReportPaths = null)
{
    public int Copied => Files.Count(f => f.Outcome == CopyOutcome.Copied);
    public int Skipped => Files.Count(f => f.Outcome == CopyOutcome.SkippedIdentical);
    public int Failed => Files.Count(f => f.Outcome == CopyOutcome.Failed);
}

/// <summary>
/// The one entry point front ends use: find cards, preview a copy, run it.
/// No UI code here (ADR-003).
/// </summary>
public sealed class ShootDropService
{
    private readonly IVolumeProvider _volumes;
    private IReadOnlyList<CameraProfile> _profiles;
    private readonly TimeProvider _clock;

    public ShootDropService(IVolumeProvider volumes, IReadOnlyList<CameraProfile> profiles, TimeProvider? clock = null)
    {
        _volumes = volumes;
        _profiles = profiles;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The camera profiles in use. Replace after the user adds, edits or deletes a camera in the app.</summary>
    public IReadOnlyList<CameraProfile> Profiles
    {
        get => _profiles;
        set => _profiles = value;
    }

    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    public static string DefaultProjectName(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Plugged-in cards, excluding the drive that holds the projects folder.</summary>
    public IReadOnlyList<DetectedCard> DetectCards(AppSettings settings)
    {
        var detector = new ProfileDetector(_profiles, settings.BuildFileTypes());
        var projectsRoot = settings.ProjectsRoot.Length > 0 ? Path.GetFullPath(settings.ProjectsRoot) : null;
        return _volumes.List()
            .Where(v => projectsRoot is null || !IsInside(projectsRoot, v.Path))
            .Select(detector.Detect)
            // An unknown volume only counts as a card if it looks like one (has DCIM). This hides
            // installer disks like "LUMIXApplications" and backup drives (found 23 Nov 2026).
            .Where(c => c.IsKnown || Directory.Exists(Path.Combine(c.Volume.Path, "DCIM")))
            .ToList();
    }

    public CopyPreview Preview(CopyRequest request, AppSettings settings, IReadOnlyList<DetectedCard>? cards = null)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectsRoot))
            throw new InvalidOperationException("Choose where projects go first (ProjectsRoot is empty).");

        cards ??= DetectCards(settings);
        var scanner = new CardScanner(settings.BuildFileTypes());
        var (from, to) = request.Date.Resolve(Today);

        var selected = cards
            .Where(c => request.CardKeys is null || request.CardKeys.Any(c.MatchesKey))
            .Select(c => new CardFiles(c, scanner.Scan(c.Volume.Path, request.Kinds, from, to)))
            .Where(cf => cf.Files.Count > 0)
            .ToList();

        if (request.Splits is { Count: > 0 } splits)
            return new CopyPreview(request, selected, BuildSplit(request, splits, selected, settings));

        var projectRoot = Path.Combine(settings.ProjectsRoot, SanitizeProjectName(request.ProjectName));
        var plan = CopyPlanner.Build(projectRoot, selected, settings.Routing);
        return new CopyPreview(request, selected, plan);
    }

    /// <summary>
    /// Spreads clips over the split's projects by the time they were recorded (their last-write time, which on
    /// cameras is when recording stopped). Parts with the same project name share one project folder.
    /// </summary>
    private static CopyPlan BuildSplit(CopyRequest request, IReadOnlyList<ProjectSplit> splits, IReadOnlyList<CardFiles> cards, AppSettings settings)
    {
        string NameOf(ProjectSplit split) =>
            SanitizeProjectName(string.IsNullOrWhiteSpace(split.ProjectName) ? request.ProjectName : split.ProjectName);

        var names = splits.Select(NameOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var perName = names.ToDictionary(n => n, _ => new List<CardFiles>(), StringComparer.OrdinalIgnoreCase);
        var unassigned = new List<UnassignedFile>();

        foreach (var (card, files) in cards)
        {
            var buckets = names.ToDictionary(n => n, _ => new List<SourceFile>(), StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (ProjectSplits.Assign(splits, file.Modified) is { } i) buckets[NameOf(splits[i])].Add(file);
                else unassigned.Add(new UnassignedFile(card.Name, file));
            }
            foreach (var (name, bucket) in buckets)
                if (bucket.Count > 0) perName[name].Add(new CardFiles(card, bucket));
        }

        var parts = names
            .Select(n => (Window: splits.First(sp => NameOf(sp).Equals(n, StringComparison.OrdinalIgnoreCase)).Window,
                          Plan: CopyPlanner.Build(Path.Combine(settings.ProjectsRoot, n), perName[n], settings.Routing)))
            .ToList();
        return CopyPlan.Combine(parts, unassigned);
    }

    /// <summary>
    /// "Split by breaks": recording sessions in the clips of <paramref name="preview"/>, named Part 1, Part 2…
    /// Empty when the clips span more than one day.
    /// </summary>
    public static IReadOnlyList<ProjectSplit> SuggestSplits(CopyPreview preview, TimeSpan? minBreak = null) =>
        SessionSplitter.Suggest(preview.Cards.SelectMany(c => c.Files).Select(f => f.Modified), minBreak)
            .Select((w, i) => new ProjectSplit($"Part {i + 1}", w))
            .ToList();

    /// <summary>Creates the project from the template, copies and verifies, writes the report, remembers the setup.</summary>
    public async Task<CopyRunResult> RunAsync(
        CopyPreview preview,
        AppSettings settings,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        CopyJournal? journal = null)
    {
        if (!preview.Plan.CanRun)
        {
            var why = preview.Plan.Unrouted.Count > 0
                ? "Choose a folder for: " + string.Join(", ", preview.Plan.Unrouted.Select(c => c.Name))
                : "Nothing to copy.";
            throw new InvalidOperationException(why);
        }

        // With a journal, a copy cut short can be resumed. When this preview is the rest of an unfinished copy
        // (PrepareResume), the journal is kept and the files finished earlier count towards the result and reports.
        var previous = journal?.Load();
        var resuming = previous is not null && preview.Plan.Items.All(i => previous.Header.Items.Any(h => h.Destination == i.Destination));
        var earlier = resuming ? previous!.DoneResults : [];
        var now = _clock.GetLocalNow().DateTime;
        if (journal is not null && !resuming) journal.Begin(preview, now);

        var parts = preview.Plan.AllParts.Where(p => p.Items.Count > 0).ToList();
        foreach (var part in parts)
            ProjectCreator.Create(part.ProjectRoot, settings.Template, settings.TemplateDirectory);
        var results = await new CopyEngine()
            .RunAsync(preview.Plan, progress, cancellationToken, journal is null ? null : journal.Record)
            .ConfigureAwait(false);

        // One report per project, listing only that project's files (including any finished before a resume).
        var all = earlier.Concat(results).ToList();
        var roots = resuming ? previous!.Header.Parts.Select(p => p.ProjectRoot).ToList() : parts.Select(p => p.ProjectRoot).ToList();
        var reports = new List<string>();
        foreach (var root in roots)
        {
            var mine = all.Where(r => IsUnder(r.Item.Destination, root)).ToList();
            if (mine.Count > 0) reports.Add(CopyReport.Write(root, mine, now));
        }

        // Done when every file of the whole copy is in its project; otherwise the journal stays for a resume.
        if (journal is not null && results.All(r => r.Outcome != CopyOutcome.Failed) && journal.Load() is { } after && after.Remaining.Count == 0)
            journal.Discard();

        if (!resuming) settings.LastUsed = preview.Request.ToSetup("Last used");
        return new CopyRunResult(preview.Plan, all, reports[0], reports);
    }

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>
    /// The rest of an unfinished copy, ready to run: the files not yet in their project, going to the same places
    /// as before. Files whose card isn't plugged in are left out and counted in <see cref="ResumePreview.MissingSources"/>.
    /// </summary>
    public ResumePreview PrepareResume(UnfinishedCopy unfinished, AppSettings settings)
    {
        var header = unfinished.Header;
        var remaining = unfinished.Remaining;
        var available = remaining.Where(i => File.Exists(i.Source)).ToList();
        var missing = remaining.Count - available.Count;

        var request = CopyRequest.FromSetup(header.Setup, header.ProjectName) with { Splits = header.Splits };
        var detected = DetectCards(settings);
        var cards = available.GroupBy(i => i.CardName)
            .Select(g => (Card: detected.FirstOrDefault(c => c.Name == g.Key), Files: g.Select(i => i.ToItem().Source).ToList()))
            .Where(c => c.Card is not null)
            .Select(c => new CardFiles(c.Card!, c.Files))
            .ToList();

        CopyPlan plan;
        if (header.Parts.Count > 1)
        {
            var parts = header.Parts
                .Select(p => (p.Window ?? TimeWindow.AllDay,
                              new CopyPlan(p.ProjectRoot, available.Where(i => i.ProjectRoot == p.ProjectRoot).Select(i => i.ToItem()).ToList(), [])))
                .ToList();
            plan = CopyPlan.Combine(parts, []);
        }
        else
        {
            plan = new CopyPlan(header.Parts[0].ProjectRoot, available.Select(i => i.ToItem()).ToList(), []);
        }
        return new ResumePreview(new CopyPreview(request, cards, plan), unfinished.DoneCount, missing,
            remaining.Where(i => !File.Exists(i.Source)).Select(i => i.CardName).Distinct().ToList());
    }

    public static string SanitizeProjectName(string name)
    {
        var cleaned = name.Replace("\"", "").Replace("\\", "").Replace('/', '-').Replace(':', '-').Trim();
        if (cleaned.Length == 0 || cleaned is "." or "..")
            throw new ArgumentException("Project name is empty.", nameof(name));
        return cleaned;
    }

    private static bool IsInside(string path, string folder)
    {
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
