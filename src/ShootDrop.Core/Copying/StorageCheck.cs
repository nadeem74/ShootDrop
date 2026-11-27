using System.Globalization;

namespace ShootDrop.Core.Copying;

public enum StorageProblem
{
    None,

    /// <summary>The projects folder is on an external drive that isn't plugged in.</summary>
    DriveMissing,

    /// <summary>Less free space than the copy needs.</summary>
    NotEnoughSpace,

    /// <summary>Free space couldn't be read; the copy may still work.</summary>
    Unknown,
}

/// <summary>Whether the projects drive can take a copy, with a message for the person.</summary>
public sealed record StorageStatus(StorageProblem Problem, string Message, string DriveName)
{
    /// <summary>True when copying must not start: the drive is missing or too full.</summary>
    public bool Blocks => Problem is StorageProblem.DriveMissing or StorageProblem.NotEnoughSpace;
}

/// <summary>
/// Checks the projects drive before a copy. Catches the two problems that matter most on a shoot day:
/// the external SSD isn't plugged in (copying would quietly fill the Mac's own disk under /Volumes),
/// and there isn't enough space left.
/// </summary>
public static class StorageCheck
{
    /// <summary>Free bytes on the drive holding <paramref name="path"/>, or null if it can't be read.</summary>
    public static long? FreeBytes(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && full.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.RootDirectory.FullName.Length)
                .FirstOrDefault();
            return drive?.AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static StorageStatus Evaluate(string projectsRoot, long bytesNeeded, Func<string, long?> freeBytes, Func<string, bool> directoryExists)
    {
        var external = ExternalDriveRoot(projectsRoot);
        var name = external is null ? "this computer's disk" : $"“{Path.GetFileName(external)}”";

        if (external is not null && !directoryExists(external))
            return new StorageStatus(StorageProblem.DriveMissing,
                $"The projects drive {name} isn't connected. Plug it in, or choose another projects folder in Settings.", name);

        var free = freeBytes(external ?? projectsRoot);
        if (free is null)
            return new StorageStatus(StorageProblem.Unknown, $"Couldn't read the free space on {name}.", name);

        if (bytesNeeded > free.Value)
            return new StorageStatus(StorageProblem.NotEnoughSpace,
                $"Not enough space on {name}: {Bytes(free.Value)} free, this copy needs {Bytes(bytesNeeded)}. Free up space or copy fewer files.", name);

        return new StorageStatus(StorageProblem.None, $"Enough space on {name}: {Bytes(free.Value)} free, this needs {Bytes(bytesNeeded)}", name);
    }

    /// <summary>
    /// The root of the removable drive holding <paramref name="path"/>: /Volumes/NAME on macOS, /media/USER/NAME or
    /// /run/media/USER/NAME on Linux, D:\ etc. on Windows (not C:\). Null when the path is on the main disk.
    /// </summary>
    public static string? ExternalDriveRoot(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "Volumes") return "/Volumes/" + parts[1];
        if (parts.Length >= 3 && parts[0] == "media") return $"/media/{parts[1]}/{parts[2]}";
        if (parts.Length >= 4 && parts[0] == "run" && parts[1] == "media") return $"/run/media/{parts[2]}/{parts[3]}";
        if (path.Length >= 3 && path[1] == ':' && char.IsLetter(path[0]) && char.ToUpperInvariant(path[0]) != 'C')
            return path[..3];
        return null;
    }

    public static string Bytes(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return unit == 0 ? $"{bytes} bytes" : value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
