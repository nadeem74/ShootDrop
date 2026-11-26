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
        CancellationToken cancellationToken = default)
    {
        if (!preview.Plan.CanRun)
        {
            var why = preview.Plan.Unrouted.Count > 0
                ? "Choose a folder for: " + string.Join(", ", preview.Plan.Unrouted.Select(c => c.Name))
                : "Nothing to copy.";
            throw new InvalidOperationException(why);
        }

        var parts = preview.Plan.AllParts.Where(p => p.Items.Count > 0).ToList();
        foreach (var part in parts)
            ProjectCreator.Create(part.ProjectRoot, settings.Template, settings.TemplateDirectory);
        var results = await new CopyEngine().RunAsync(preview.Plan, progress, cancellationToken).ConfigureAwait(false);

        // One report per project, listing only that project's files.
        var now = _clock.GetLocalNow().DateTime;
        var reports = new List<string>();
        foreach (var part in parts)
        {
            var mine = new HashSet<CopyItem>(part.Items, ReferenceEqualityComparer.Instance);
            reports.Add(CopyReport.Write(part.ProjectRoot, results.Where(r => mine.Contains(r.Item)).ToList(), now));
        }

        settings.LastUsed = preview.Request.ToSetup("Last used");
        return new CopyRunResult(preview.Plan, results, reports[0], reports);
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
