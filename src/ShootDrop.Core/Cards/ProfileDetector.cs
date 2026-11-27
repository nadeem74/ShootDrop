using ShootDrop.Core.Media;

namespace ShootDrop.Core.Cards;

/// <summary>A plugged-in card and the device it came from (null when unknown).</summary>
public sealed record DetectedCard(VolumeInfo Volume, CameraProfile? Profile)
{
    /// <summary>
    /// Key used to remember choices for this card. For now the volume name;
    /// TODO(v0.2): use the volume UUID (diskutil / DriveInfo serial) so renamed cards still match.
    /// </summary>
    public string CardId => Volume.Name;

    public string Name => Volume.Name;
    public bool IsKnown => Profile is not null;

    /// <summary>True if a setup's card key refers to this card (by profile id or card id).</summary>
    public bool MatchesKey(string key) =>
        string.Equals(key, CardId, StringComparison.OrdinalIgnoreCase)
        || (Profile is not null && string.Equals(key, Profile.Id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ProfileDetector(IReadOnlyList<CameraProfile> profiles, FileTypeCatalog types)
{
    private static readonly EnumerationOptions Shallow = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 3,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    public DetectedCard Detect(VolumeInfo volume) => new(volume, FindProfile(volume));

    private CameraProfile? FindProfile(VolumeInfo volume)
    {
        var root = volume.Path;
        var dcim = Path.Combine(root, "DCIM");
        var hasDcim = Directory.Exists(dcim);

        // 1. Strong signals: marker paths, DCIM folder names, file prefixes.
        foreach (var p in profiles)
        {
            var m = p.Match;
            if (m.MarkerPaths.Any(x => Directory.Exists(Path.Combine(root, x)) || File.Exists(Path.Combine(root, x))))
                return p;
            if (hasDcim && m.DcimFolderContains.Length > 0 && SafeDirs(dcim).Any(d =>
                    m.DcimFolderContains.Any(s => Path.GetFileName(d).Contains(s, StringComparison.OrdinalIgnoreCase))))
                return p;
            if (hasDcim && m.FilePrefixes.Length > 0 && SafeFiles(dcim).Any(f =>
                    m.FilePrefixes.Any(px => Path.GetFileName(f).StartsWith(px, StringComparison.OrdinalIgnoreCase))))
                return p;
        }

        // 2. Audio recorders: no DCIM, has audio. Checked before the volume name,
        //    because a DJI Mic card may be named "DJI ..." too.
        if (!hasDcim)
        {
            var audioProfile = profiles.FirstOrDefault(p => p.Match.AudioOnlyWithoutDcim);
            if (audioProfile is not null && LooksLikeAudioRecorder(root))
                return audioProfile;
        }

        // 3. Volume name, only for cards that have a DCIM folder. Without this, a mounted
        //    installer like "LUMIX Applications" was taken for a Lumix card (found 23 Nov 2026).
        if (!hasDcim) return null;
        foreach (var p in profiles)
        {
            if (p.Match.VolumeNameContains.Any(s => volume.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return p;
        }

        return null;
    }

    /// <summary>
    /// An audio recorder's card holds (almost) only audio. A backup drive that happens to contain a WAV somewhere
    /// next to videos and project folders is not one ("HDD 2TB" was taken for a DJI Mic, found 27 Nov 2026).
    /// </summary>
    private bool LooksLikeAudioRecorder(string root)
    {
        int audio = 0, other = 0;
        foreach (var f in SafeFiles(root).Take(400))
        {
            if (types.KindOf(f) == FileKind.Audio) audio++;
            else other++;
        }
        return audio > 0 && audio >= 4 * other;
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", Shallow); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
