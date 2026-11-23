using System.Runtime.Versioning;

namespace ShootDrop.Platform.Mac;

/// <summary>macOS virtual key codes (Carbon Events.h, US layout positions) for the keys a shortcut can use.</summary>
[SupportedOSPlatform("macos")]
public static class KeyCodes
{
    private static readonly Dictionary<string, uint> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0, ["S"] = 1, ["D"] = 2, ["F"] = 3, ["H"] = 4, ["G"] = 5, ["Z"] = 6, ["X"] = 7, ["C"] = 8, ["V"] = 9,
        ["B"] = 11, ["Q"] = 12, ["W"] = 13, ["E"] = 14, ["R"] = 15, ["Y"] = 16, ["T"] = 17,
        ["1"] = 18, ["2"] = 19, ["3"] = 20, ["4"] = 21, ["6"] = 22, ["5"] = 23, ["9"] = 25, ["7"] = 26, ["8"] = 28, ["0"] = 29,
        ["O"] = 31, ["U"] = 32, ["I"] = 34, ["P"] = 35, ["L"] = 37, ["J"] = 38, ["K"] = 40, ["N"] = 45, ["M"] = 46,
        ["F1"] = 122, ["F2"] = 120, ["F3"] = 99, ["F4"] = 118, ["F5"] = 96, ["F6"] = 97,
        ["F7"] = 98, ["F8"] = 100, ["F9"] = 101, ["F10"] = 109, ["F11"] = 103, ["F12"] = 111,
    };

    public static bool TryGet(string key, out uint code) => Codes.TryGetValue(key, out code);
}
