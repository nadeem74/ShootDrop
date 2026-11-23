using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;

namespace ShootDrop.Core.Projects;

/// <summary>
/// Where each source goes (FR-18). Checked in this order:
/// file kind (e.g. Photo → Images, Proxy → skip), then the remembered folder for this card,
/// then the folder for its camera profile, then the profile's default folder.
/// </summary>
public sealed class Routing
{
    /// <summary>Kind overrides. An empty string means "don't copy".</summary>
    public Dictionary<FileKind, string> ByKind { get; set; } = new();

    /// <summary>Profile id → folder.</summary>
    public Dictionary<string, string> ByProfile { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Card id → folder, for cards the user routed by hand (unknown cards).</summary>
    public Dictionary<string, string> ByCard { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default audio folder. DJI cameras save the mic track as a separate .AAC next to each .MP4.</summary>
    public const string DefaultAudioFolder = "Audio/DJI Mic";

    public static Routing Default() => new()
    {
        ByKind = new() { [FileKind.Audio] = DefaultAudioFolder, [FileKind.Photo] = "Images", [FileKind.Proxy] = "" },
    };

    /// <returns>The project-relative folder, or null when the user must choose (unknown card).</returns>
    public RouteResult Resolve(DetectedCard card, FileKind kind)
    {
        if (ByKind.TryGetValue(kind, out var byKind))
            return byKind.Length == 0 ? RouteResult.Skip : RouteResult.To(byKind);
        if (ByCard.TryGetValue(card.CardId, out var byCard))
            return byCard.Length == 0 ? RouteResult.Skip : RouteResult.To(byCard);
        if (card.Profile is { } p)
        {
            if (ByProfile.TryGetValue(p.Id, out var byProfile))
                return byProfile.Length == 0 ? RouteResult.Skip : RouteResult.To(byProfile);
            if (p.DefaultFolder.Length > 0)
                return RouteResult.To(p.DefaultFolder);
        }
        return RouteResult.Unrouted;
    }
}

public readonly record struct RouteResult(RouteStatus Status, string Folder)
{
    public static RouteResult Skip => new(RouteStatus.Skip, "");
    public static RouteResult Unrouted => new(RouteStatus.Unrouted, "");
    public static RouteResult To(string folder) => new(RouteStatus.Copy, folder.Trim('/'));
}

public enum RouteStatus
{
    Copy,
    Skip,
    Unrouted,
}
