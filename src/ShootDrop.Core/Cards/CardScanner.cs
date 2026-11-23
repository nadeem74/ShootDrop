using ShootDrop.Core.Media;

namespace ShootDrop.Core.Cards;

public sealed record SourceFile(string Path, long Size, DateTime Modified, FileKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Finds the files on a card that match the chosen dates and file types. Read-only.</summary>
public sealed class CardScanner(FileTypeCatalog types)
{
    private static readonly string[] SystemFolders = [".Spotlight-V100", ".Trashes", ".fseventsd", "System Volume Information", "$RECYCLE.BIN"];

    private static readonly EnumerationOptions Deep = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 8,
        IgnoreInaccessible = true,
        // On macOS/Linux, .NET marks dot-files (incl. AppleDouble "._x" files) as Hidden.
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <param name="from">First recording date, inclusive (local time).</param>
    /// <param name="to">Last recording date, inclusive (local time).</param>
    public IReadOnlyList<SourceFile> Scan(string cardRoot, IReadOnlySet<FileKind> kinds, DateOnly from, DateOnly to)
    {
        var result = new List<SourceFile>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(cardRoot, "*", Deep);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("._", StringComparison.Ordinal)) continue;
            if (SystemFolders.Any(s => path.Contains(Path.DirectorySeparatorChar + s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;

            var kind = types.KindOf(path);
            if (kind is null || !kinds.Contains(kind.Value)) continue;

            // Recording date = file's last-write time, as the old script did.
            // TODO(v0.2, PRD "Later"): read the creation date from the clip's metadata.
            var info = new FileInfo(path);
            var date = DateOnly.FromDateTime(info.LastWriteTime);
            if (date < from || date > to) continue;

            result.Add(new SourceFile(info.FullName, info.Length, info.LastWriteTime, kind.Value));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return result;
    }
}
