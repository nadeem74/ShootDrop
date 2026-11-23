using System.Text.RegularExpressions;
using ShootDrop.Core.Media;

namespace ShootDrop.Core.Cards;

/// <summary>
/// Looks at a plugged-in card and suggests how to recognise it, so a user can add a camera without knowing
/// anything about card layouts. Only reads folder and file names; never writes to the card.
/// </summary>
public static partial class ProfileSuggester
{
    /// <summary>Names cards get when nobody named them; useless for telling cameras apart.</summary>
    private static readonly string[] GenericVolumeNames = ["NO NAME", "UNTITLED", "Untitled", "SD", "SDCARD", "EOS_DIGITAL", "CARD"];

    public static ProfileMatch Suggest(string volumePath, string volumeName, FileTypeCatalog? types = null)
    {
        types ??= FileTypeCatalog.Default;
        var dcim = Path.Combine(volumePath, "DCIM");
        var hasDcim = Directory.Exists(dcim);

        // Marker folders some brands create, e.g. PRIVATE/PANASONIC, PRIVATE/M4ROOT (Sony).
        var markers = new List<string>();
        var priv = Path.Combine(volumePath, "PRIVATE");
        if (Directory.Exists(priv))
            markers.AddRange(SafeDirs(priv).Select(d => "PRIVATE/" + Path.GetFileName(d)));

        // DCIM subfolders are "100" + a brand tag, e.g. 100GOPRO, 101_PANA, 100MEDIA. Keep the tag.
        var dcimTags = hasDcim
            ? SafeDirs(dcim).Select(d => FolderTag().Match(Path.GetFileName(d)).Groups[1].Value.Trim('_'))
                .Where(t => t.Length >= 3 && !t.Equals("MEDIA", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        // The letters most media files start with, e.g. DJI_, GX, DSC_, C0 (clip names).
        var mediaNames = (hasDcim ? SafeFiles(dcim) : SafeFiles(volumePath))
            .Where(f => types.KindOf(f) is not null)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
        var prefixes = mediaNames
            .Select(n => FilePrefix().Match(n).Value)
            .Where(p => p.Length >= 2)
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() * 2 >= mediaNames.Count) // shared by at least half the files
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(1)
            .ToArray();

        var name = volumeName.Trim();
        var volume = name.Length >= 3 && !GenericVolumeNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? new[] { name.ToLowerInvariant() }
            : [];

        var audioOnly = !hasDcim && mediaNames.Count > 0 && mediaNames.All(n => types.KindOf(n) == FileKind.Audio);

        return new ProfileMatch
        {
            MarkerPaths = markers.ToArray(),
            DcimFolderContains = dcimTags,
            FilePrefixes = prefixes,
            VolumeNameContains = volume,
            AudioOnlyWithoutDcim = audioOnly,
        };
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).Where(d => !Path.GetFileName(d).StartsWith('.')).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                .Where(f => !Path.GetFileName(f).StartsWith('.')).Take(2000).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    [GeneratedRegex(@"^\d{3}(.*)$")]
    private static partial Regex FolderTag();

    // Leading letters, plus an underscore if there is one: "DJI_0001.MP4" → "DJI_", "GX010023.MP4" → "GX".
    [GeneratedRegex(@"^[A-Za-z]+_?")]
    private static partial Regex FilePrefix();
}
