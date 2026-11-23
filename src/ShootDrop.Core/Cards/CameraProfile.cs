using System.Reflection;
using System.Text.Json;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Cards;

/// <summary>How ShootDrop recognises one kind of device. Loaded from /profiles/*.json.</summary>
public sealed record CameraProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Project subfolder this device's files go to unless the user routes them elsewhere.</summary>
    public string DefaultFolder { get; init; } = "";

    public ProfileMatch Match { get; init; } = new();
}

public sealed record ProfileMatch
{
    /// <summary>Paths that only this device creates, e.g. PRIVATE/PANASONIC. Strongest signal.</summary>
    public string[] MarkerPaths { get; init; } = [];

    /// <summary>File-name prefixes inside DCIM, e.g. DJI_.</summary>
    public string[] FilePrefixes { get; init; } = [];

    /// <summary>Text in DCIM subfolder names, e.g. 100_PANA.</summary>
    public string[] DcimFolderContains { get; init; } = [];

    /// <summary>Text in the card's volume name. Weaker: users rename cards.</summary>
    public string[] VolumeNameContains { get; init; } = [];

    /// <summary>Matches a card with no DCIM folder that holds audio files (field recorders, DJI Mic).</summary>
    public bool AudioOnlyWithoutDcim { get; init; }
}

public static class ProfileCatalog
{
    private const string ResourcePrefix = "ShootDrop.Profiles.";

    /// <summary>The profiles shipped in /profiles, embedded at build time.</summary>
    public static IReadOnlyList<CameraProfile> BuiltIn()
    {
        var asm = typeof(ProfileCatalog).Assembly;
        var list = new List<CameraProfile>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order())
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            list.Add(Read(stream, name));
        }
        return list;
    }

    /// <summary>Built-in profiles plus the user's own; a user profile with the same id wins.</summary>
    public static IReadOnlyList<CameraProfile> Load(string? userProfilesDirectory)
    {
        var byId = BuiltIn().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        if (userProfilesDirectory is not null && Directory.Exists(userProfilesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(userProfilesDirectory, "*.json").Order())
            {
                using var stream = File.OpenRead(file);
                var p = Read(stream, file);
                byId[p.Id] = p;
            }
        }
        return byId.Values.ToList();
    }

    private static CameraProfile Read(Stream stream, string source) =>
        JsonSerializer.Deserialize<CameraProfile>(stream, Json.Options)
        ?? throw new InvalidDataException($"Empty camera profile: {source}");
}
