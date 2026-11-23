using ShootDrop.Core.Cards;
using ShootDrop.Core.Projects;

namespace ShootDrop.Core.Copying;

public sealed record CopyItem(SourceFile Source, string Destination, string CardName, string Folder);

/// <param name="ProjectRoot">Full path of the project folder.</param>
/// <param name="Items">Files to copy, in order.</param>
/// <param name="Unrouted">Cards with files but no destination yet (unknown cards). Copying is blocked until routed.</param>
public sealed record CopyPlan(string ProjectRoot, IReadOnlyList<CopyItem> Items, IReadOnlyList<DetectedCard> Unrouted)
{
    public long TotalBytes => Items.Sum(i => i.Source.Size);
    public bool CanRun => Items.Count > 0 && Unrouted.Count == 0;
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
