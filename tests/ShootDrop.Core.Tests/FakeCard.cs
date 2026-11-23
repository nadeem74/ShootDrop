namespace ShootDrop.Core.Tests;

/// <summary>
/// Builds a pretend camera card in a temp folder: real files, real folder layout, chosen timestamps.
/// Deleted on Dispose.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string name = "shootdrop")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Sub(string relative)
    {
        var p = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}

public sealed class FakeCard
{
    public string Root { get; }

    public FakeCard(TempDir parent, string volumeName)
    {
        Root = parent.Sub(volumeName);
    }

    /// <summary>Adds a file of <paramref name="bytes"/> pseudo-random bytes, recorded on <paramref name="recorded"/>.</summary>
    public string Add(string relativePath, DateTime recorded, int bytes = 4096, int seed = 1)
    {
        var path = System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        new Random(seed + relativePath.GetHashCode(StringComparison.Ordinal)).NextBytes(data);
        File.WriteAllBytes(path, data);
        File.SetLastWriteTime(path, recorded);
        return path;
    }

    public void AddFolder(string relativePath) =>
        Directory.CreateDirectory(System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    /// <summary>Snapshot of every file's size, time and bytes, to prove a card was not changed.</summary>
    public IReadOnlyDictionary<string, string> Fingerprint() =>
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                f => f,
                f => $"{new FileInfo(f).Length}|{File.GetLastWriteTimeUtc(f):O}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))}");
}

public static class Days
{
    // Fixed "today" for tests: Mon 23 Nov 2026, local time.
    public static readonly DateOnly Today = new(2026, 11, 23);
    public static DateTime At(int day, int hour = 12) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Local);
}

/// <summary>A clock frozen at noon on <see cref="Days.Today"/> in local time.</summary>
public sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Days.At(23)).ToUniversalTime();
}
