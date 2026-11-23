using System.Text.Json;
using System.Text.Json.Serialization;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;

namespace ShootDrop.Core.Settings;

/// <summary>
/// A saved way of copying (FR-19): which cards, which dates, which file types.
/// <see cref="CardKeys"/> null means "all cards that have matching files".
/// </summary>
public sealed record Setup(string Name, IReadOnlyList<string>? CardKeys, DateRule Date, IReadOnlyList<FileKind> Kinds)
{
    public static Setup ShootDay { get; } = new("Shoot day", null, DateRule.Today, [FileKind.Video, FileKind.Audio]);
}

/// <summary>Everything ShootDrop remembers between runs.</summary>
public sealed class AppSettings
{
    /// <summary>Settings format version, for upgrading older settings.json files.</summary>
    public int Version { get; set; }

    public const int CurrentVersion = 2;

    /// <summary>Brings settings saved by an older ShootDrop up to date.</summary>
    public void Upgrade()
    {
        if (Version < 2)
        {
            // v2: audio files get their own folder instead of following the camera's video (found 23 Nov 2026:
            // the Pocket's .AAC mic tracks landed in Video/Pocket 4P).
            Routing.ByKind.TryAdd(FileKind.Audio, Routing.DefaultAudioFolder);
        }
        Version = CurrentVersion;
    }

    /// <summary>Where projects are created, e.g. "/Volumes/My SSD/Video Projects".</summary>
    public string ProjectsRoot { get; set; } = "";

    /// <summary>Optional folder copied into every new project (the user's "Project template").</summary>
    public string? TemplateDirectory { get; set; }

    public FolderTemplate Template { get; set; } = FolderTemplate.MyTemplate;
    public Routing Routing { get; set; } = Routing.Default();
    public List<Setup> Setups { get; set; } = [Setup.ShootDay];

    /// <summary>The exact choices of the last copy; "Copy like last time" uses this (FR-20).</summary>
    public Setup? LastUsed { get; set; }

    /// <summary>After copying: open the project in Finder.</summary>
    public bool OpenFinderWhenDone { get; set; } = true;

    /// <summary>After copying: eject the cards that were copied.</summary>
    public bool EjectWhenDone { get; set; }

    /// <summary>Whether the smart box is switched on in the main window.</summary>
    public bool SmartBoxOn { get; set; } = true;

    /// <summary>Extra extensions the user added, e.g. "braw" → Video.</summary>
    public Dictionary<string, FileKind> ExtraExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public Setup CurrentSetup => LastUsed ?? Setups.FirstOrDefault() ?? Setup.ShootDay;

    public Setup? FindSetup(string name) =>
        name.Equals("last", StringComparison.OrdinalIgnoreCase)
            ? CurrentSetup
            : Setups.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void SaveSetup(Setup setup)
    {
        Setups.RemoveAll(s => s.Name.Equals(setup.Name, StringComparison.OrdinalIgnoreCase));
        Setups.Add(setup);
    }

    public FileTypeCatalog BuildFileTypes()
    {
        var catalog = FileTypeCatalog.Default;
        foreach (var (ext, kind) in ExtraExtensions)
        {
            catalog = catalog.With(ext, kind);
        }
        return catalog;
    }
}

/// <summary>Loads and saves settings.json. Writes go to a temp file first, then replace, so a crash can't corrupt it.</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public static string DefaultDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(home, "Library", "Application Support", "ShootDrop");
        }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShootDrop");
    }

    public static SettingsStore Default() => new(System.IO.Path.Combine(DefaultDirectory(), "settings.json"));

    /// <summary>User camera profiles live next to settings, in "profiles".</summary>
    public string ProfilesDirectory => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "profiles");

    public AppSettings Load()
    {
        AppSettings settings;
        if (!File.Exists(Path))
        {
            settings = new AppSettings();
        }
        else
        {
            using var stream = File.OpenRead(Path);
            settings = JsonSerializer.Deserialize<AppSettings>(stream, Json.Options) ?? new AppSettings();
        }
        settings.Upgrade();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json.Options));
        File.Move(tmp, Path, overwrite: true);
    }
}
