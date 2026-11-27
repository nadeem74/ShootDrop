using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;

namespace ShootDrop.Core.Copying;

public sealed record CopyItem(SourceFile Source, string Destination, string CardName, string Folder);

/// <summary>One project of a copy. A normal copy has one part; a time split ("till 1 pm into A…") has several.</summary>
/// <param name="Name">Project folder name.</param>
/// <param name="Window">Time of day this part takes, or null for a normal (unsplit) copy.</param>
public sealed record CopyPart(string Name, string ProjectRoot, TimeWindow? Window, IReadOnlyList<CopyItem> Items)
{
    public long TotalBytes => Items.Sum(i => i.Source.Size);
}

/// <summary>A clip on the chosen dates whose time is outside every part of a split. Shown, never copied.</summary>
public sealed record UnassignedFile(string CardName, SourceFile Source);

/// <param name="ProjectRoot">Full path of the project folder (the first project when the copy is split).</param>
/// <param name="Items">Files to copy, in order, across all parts.</param>
/// <param name="Unrouted">Cards with files but no destination yet (unknown cards). Copying is blocked until routed.</param>
/// <param name="Parts">Projects of a time split; null for a normal one-project copy.</param>
/// <param name="Unassigned">Clips outside every time window of a split.</param>
public sealed record CopyPlan(
    string ProjectRoot,
    IReadOnlyList<CopyItem> Items,
    IReadOnlyList<DetectedCard> Unrouted,
    IReadOnlyList<CopyPart>? Parts = null,
    IReadOnlyList<UnassignedFile>? Unassigned = null)
{
    public long TotalBytes => Items.Sum(i => i.Source.Size);
    public bool CanRun => Items.Count > 0 && Unrouted.Count == 0;
    public bool IsSplit => Parts is not null;

    /// <summary>Every project this copy writes to; one part for a normal copy.</summary>
    public IReadOnlyList<CopyPart> AllParts => Parts ?? [new CopyPart(Path.GetFileName(ProjectRoot), ProjectRoot, null, Items)];

    public IReadOnlyList<UnassignedFile> UnassignedFiles => Unassigned ?? [];

    /// <summary>Puts several one-project plans together into one split plan the copy engine can run in one go.</summary>
    public static CopyPlan Combine(IReadOnlyList<(TimeWindow Window, CopyPlan Plan)> parts, IReadOnlyList<UnassignedFile> unassigned)
    {
        if (parts.Count == 0) throw new ArgumentException("A split needs at least one part.", nameof(parts));
        var items = parts.SelectMany(p => p.Plan.Items).ToList();
        var unrouted = parts.SelectMany(p => p.Plan.Unrouted).Distinct().ToList();
        var copyParts = parts.Select(p => new CopyPart(Path.GetFileName(p.Plan.ProjectRoot), p.Plan.ProjectRoot, p.Window, p.Plan.Items)).ToList();
        return new CopyPlan(parts[0].Plan.ProjectRoot, items, unrouted, copyParts, unassigned);
    }
}

public sealed record CardFiles(DetectedCard Card, IReadOnlyList<SourceFile> Files);

public static class CopyPlanner
{
    /// <summary>
    /// Decides where every file goes. Files are placed flat inside their folder (as the old script did).
    /// If two cards have a file with the same name in the same folder (two Pockets both writing
    /// DJI_0001.MP4), the second gets the card name added. Files already on disk are handled at copy time.
    /// </summary>
    public static CopyPlan Build(string projectRoot, IEnumerable<CardFiles> cards, Routing routing)
    {
        var items = new List<CopyItem>();
        var unrouted = new List<DetectedCard>();
        // APFS and NTFS are case-insensitive by default; plan for that everywhere.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (card, files) in cards)
        {
            var cardUnrouted = false;
            foreach (var file in files)
            {
                var route = routing.Resolve(card, file.Kind);
                if (route.Status == RouteStatus.Skip) continue;
                if (route.Status == RouteStatus.Unrouted)
                {
                    cardUnrouted = true;
                    continue;
                }

                var folderPath = ProjectCreator.ToPath(projectRoot, route.Folder);
                var dest = Path.Combine(folderPath, file.Name);
                if (!taken.Add(dest))
                {
                    dest = UniqueName(folderPath, file.Name, Sanitize(card.Name), taken);
                }
                items.Add(new CopyItem(file, dest, card.Name, route.Folder));
            }

            if (cardUnrouted) unrouted.Add(card);
        }

        return new CopyPlan(Path.GetFullPath(projectRoot), items, unrouted);
    }

    private static string UniqueName(string folder, string fileName, string suffix, HashSet<string> taken)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = Path.Combine(folder, $"{stem}_{suffix}{ext}");
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = Path.Combine(folder, $"{stem}_{suffix}_{n}{ext}");
        }
        return candidate;
    }

    internal static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray();
        return new string(chars).Trim('-');
    }
}
