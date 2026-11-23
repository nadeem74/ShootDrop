namespace ShootDrop.Core.Cards;

public sealed record VolumeInfo(string Path, string Name);

/// <summary>Lists places that might be camera cards.</summary>
public interface IVolumeProvider
{
    IReadOnlyList<VolumeInfo> List();
}

/// <summary>Removable volumes on this computer.</summary>
public sealed class SystemVolumeProvider : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> List()
    {
        if (OperatingSystem.IsWindows())
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                .Select(d => new VolumeInfo(d.RootDirectory.FullName, string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : d.VolumeLabel))
                .ToList();
        }

        var roots = new List<string>();
        if (OperatingSystem.IsMacOS())
        {
            roots.Add("/Volumes");
        }
        else
        {
            var user = Environment.UserName;
            roots.Add(Path.Combine("/media", user));
            roots.Add(Path.Combine("/run/media", user));
        }

        var result = new List<VolumeInfo>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var dir in SafeDirectories(root))
            {
                var info = new DirectoryInfo(dir);
                // On macOS the boot disk appears in /Volumes as a symlink to "/". Skip links.
                if (info.LinkTarget is not null) continue;
                result.Add(new VolumeInfo(info.FullName, info.Name));
            }
        }
        return result;
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>Treats given folders as cards. Used by tests and by "Copy from a folder instead…".</summary>
public sealed class FolderVolumeProvider(params string[] folders) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> List() =>
        folders.Where(Directory.Exists)
            .Select(f => new DirectoryInfo(f))
            .Select(d => new VolumeInfo(d.FullName, d.Name))
            .ToList();
}
