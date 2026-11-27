using System.Diagnostics;
using System.Text.Json.Nodes;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Phone;

/// <summary>A video on a connected iPhone, as the helper lists it. <see cref="Id"/> tells apart files with the same name.</summary>
public sealed record PhoneClip(string Device, string Id, string Name, long Size, DateTime Recorded);

/// <summary>
/// iPhone import over USB (macOS). An iPhone isn't a drive, so ShootDrop reads it with a small helper
/// (<c>shootdrop-iphone</c>, Apple's ImageCaptureCore) and brings the new videos into a staging folder that looks like
/// a card called "iPhone". From there the normal autopilot and copy engine take over: same screen, same verified copy.
/// The phone is only ever read. The staging folder is ShootDrop's own and is emptied after a successful copy.
/// </summary>
public static class PhoneImport
{
    public const string CardName = "iPhone";

    /// <summary>File that marks a staging folder as an iPhone "card" (matched by profiles/apple-iphone.json).</summary>
    public const string Marker = "SHOOTDROP_IPHONE";

    /// <summary>
    /// Where clips are staged: a hidden folder next to the projects folder, so it's on the same drive as the projects
    /// (the space check covers it) but never inside a project. Falls back to <paramref name="fallbackDirectory"/>.
    /// </summary>
    public static string StagingRoot(AppSettings settings, string fallbackDirectory)
    {
        if (settings.ProjectsRoot.Length > 0 && Path.GetDirectoryName(Path.GetFullPath(settings.ProjectsRoot)) is { Length: > 0 } parent)
            return Path.Combine(parent, ".shootdrop-iphone");
        return Path.Combine(fallbackDirectory, "iphone-staging");
    }

    public static string CardFolder(string stagingRoot) => Path.Combine(stagingRoot, CardName);

    public static string MediaFolder(string stagingRoot) => Path.Combine(CardFolder(stagingRoot), "DCIM", "100APPLE");

    /// <summary>Creates the staging "card" with its marker. Returns the folder new clips go into.</summary>
    public static string PrepareCard(string stagingRoot)
    {
        var media = MediaFolder(stagingRoot);
        Directory.CreateDirectory(media);
        var marker = Path.Combine(CardFolder(stagingRoot), Marker);
        if (!File.Exists(marker)) File.WriteAllText(marker, "Staging folder for iPhone import. Safe to delete when no copy is running.\n");
        return media;
    }

    /// <summary>Empties the staging card after its clips are safely in projects. Only ShootDrop's own folder.</summary>
    public static void Clear(string stagingRoot)
    {
        try
        {
            if (File.Exists(Path.Combine(CardFolder(stagingRoot), Marker))) Directory.Delete(CardFolder(stagingRoot), recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Which videos to bring in, by the same rule as cards: recorded after the last autopilot copy from the iPhone,
    /// or, the first time, only the most recent recording day. Clips already staged are left out.
    /// </summary>
    public static IReadOnlyList<PhoneClip> SelectNew(IReadOnlyList<PhoneClip> clips, AppSettings settings, IReadOnlySet<string>? alreadyStaged = null)
    {
        if (clips.Count == 0) return [];
        IEnumerable<PhoneClip> fresh = settings.AutoCopiedUpTo.TryGetValue(CardName, out var mark)
            ? clips.Where(c => c.Recorded > mark)
            : clips.Where(c => c.Recorded.Date == clips.Max(x => x.Recorded).Date);
        if (alreadyStaged is { Count: > 0 }) fresh = fresh.Where(c => !alreadyStaged.Contains(c.Name + "|" + c.Size));
        return fresh.OrderBy(c => c.Recorded).ToList();
    }

    /// <summary>Name|size of clips already in the staging card, so a second insert doesn't fetch them again.</summary>
    public static IReadOnlySet<string> Staged(string stagingRoot)
    {
        var media = MediaFolder(stagingRoot);
        if (!Directory.Exists(media)) return new HashSet<string>();
        return new DirectoryInfo(media).GetFiles().Select(f => f.Name + "|" + f.Length).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One line of helper output, or null if it isn't a clip.</summary>
    public static PhoneClip? ParseClip(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject o || o["id"] is null || o["recorded"] is null || o["path"] is not null) return null;
            return new PhoneClip(
                o["device"]?.GetValue<string>() ?? CardName,
                o["id"]!.GetValue<string>(),
                o["name"]?.GetValue<string>() ?? "",
                o["size"]?.GetValue<long>() ?? 0,
                DateTimeOffset.FromUnixTimeSeconds(o["recorded"]!.GetValue<long>()).LocalDateTime);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>The staging "card", when it holds clips.</summary>
public sealed class StagingVolumeProvider(Func<string?> stagingRoot) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> List()
    {
        if (stagingRoot() is not { } root) return [];
        var card = PhoneImport.CardFolder(root);
        var media = PhoneImport.MediaFolder(root);
        try
        {
            var hasClips = File.Exists(Path.Combine(card, PhoneImport.Marker)) && Directory.Exists(media) && Directory.EnumerateFiles(media).Any();
            return hasClips ? new List<VolumeInfo> { new(card, PhoneImport.CardName) } : new List<VolumeInfo>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>Several sources of cards as one: real removable drives plus the iPhone staging card.</summary>
public sealed class CompositeVolumeProvider(params IVolumeProvider[] providers) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> List() => providers.SelectMany(p => p.List()).ToList();
}

/// <summary>Runs the <c>shootdrop-iphone</c> helper and reads its JSON lines.</summary>
public sealed class IPhoneHelper(string executable)
{
    public const string FileName = "shootdrop-iphone";

    /// <summary>The helper next to the app's own files (inside ShootDrop.app on macOS), or null when it isn't there.</summary>
    public static IPhoneHelper? Find()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        return File.Exists(path) ? new IPhoneHelper(path) : null;
    }

    /// <summary>Every video on the connected iPhone. <paramref name="onLocked"/> is called if the phone must be unlocked first.</summary>
    public async Task<IReadOnlyList<PhoneClip>> ListAsync(Action onLocked, CancellationToken ct = default)
    {
        var clips = new List<PhoneClip>();
        string? error = null;
        await RunAsync(["list"], line =>
        {
            if (PhoneImport.ParseClip(line) is { } c) clips.Add(c);
            else if (line.Contains("\"locked\"", StringComparison.Ordinal)) onLocked();
            else if (Error(line) is { } e) error = e;
        }, ct).ConfigureAwait(false);
        if (error is not null && clips.Count == 0) throw new InvalidOperationException(error);
        return clips;
    }

    /// <summary>Downloads <paramref name="clips"/> into <paramref name="folder"/>. Returns how many arrived complete.</summary>
    public async Task<int> DownloadAsync(IReadOnlyList<PhoneClip> clips, string folder, Action<int, int> progress, CancellationToken ct = default)
    {
        var done = 0;
        var failed = 0;
        await RunAsync(["get", folder, .. clips.Select(c => c.Id)], line =>
        {
            if (line.Contains("\"path\"", StringComparison.Ordinal)) done++;
            else if (line.Contains("\"id\"", StringComparison.Ordinal) && line.Contains("\"error\"", StringComparison.Ordinal)) failed++;
            else return;
            progress(done + failed, clips.Count);
        }, ct).ConfigureAwait(false);
        return done;
    }

    /// <summary>Starts watching for iPhones being plugged in or out. Kill the returned process to stop.</summary>
    public Process? Watch(Action<bool, string> onChange)
    {
        var p = Start(["watch"]);
        if (p is null) return null;
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not { } line) return;
            try
            {
                if (JsonNode.Parse(line) is JsonObject o && o["event"]?.GetValue<string>() is { } ev)
                    onChange(ev == "added", o["device"]?.GetValue<string>() ?? PhoneImport.CardName);
            }
            catch (System.Text.Json.JsonException)
            {
            }
        };
        p.BeginOutputReadLine();
        return p;
    }

    private static string? Error(string line)
    {
        try { return JsonNode.Parse(line) is JsonObject o && o["status"]?.GetValue<string>() == "error" ? o["error"]?.GetValue<string>() : null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private Process? Start(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(executable) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try { return Process.Start(psi); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private async Task RunAsync(IEnumerable<string> args, Action<string> onLine, CancellationToken ct)
    {
        using var p = Start(args) ?? throw new InvalidOperationException("Couldn't start the iPhone helper.");
        using var reg = ct.Register(() => { try { p.Kill(); } catch (InvalidOperationException) { } });
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null) onLine(line);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
    }
}
