using System.Text.Json.Serialization;

namespace ShootDrop.Core.Background;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Option = 2,
    Shift = 4,
    Command = 8,
}

/// <summary>
/// A global keyboard shortcut (FR-23), e.g. ⌃⌥⌘C. <see cref="Key"/> is a letter (A–Z), a digit (0–9) or F1–F12.
/// Platform code turns it into a real key code; this type only knows the rules and how to show it.
/// </summary>
public sealed record Shortcut(ShortcutModifiers Modifiers, string Key)
{
    public static Shortcut Default { get; } = new(ShortcutModifiers.Control | ShortcutModifiers.Option | ShortcutModifiers.Command, "C");

    /// <summary>Taken by macOS or every app; a global shortcut on these would break normal use.</summary>
    private static readonly HashSet<string> CommandOnlyReserved = ["Q", "W", "H", "M", "C", "V", "X", "Z", "A", "S", "N", "O", "P", "T", "F"];

    public static bool IsSupportedKey(string key) =>
        key.Length == 1 && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0]))
        || key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 12;

    private bool IsFunctionKey => Key.Length > 1;

    /// <summary>Null if the shortcut can be used, otherwise a plain-English reason it can't.</summary>
    [JsonIgnore]
    public string? Problem
    {
        get
        {
            if (!IsSupportedKey(Key)) return "Use a letter, a number or F1–F12.";
            var strong = Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Option | ShortcutModifiers.Command);
            if (strong == ShortcutModifiers.None && !IsFunctionKey)
                return "Add ⌃ Control, ⌥ Option or ⌘ Command, so normal typing isn't caught.";
            if (Modifiers == ShortcutModifiers.Command && CommandOnlyReserved.Contains(Key))
                return $"⌘{Key} is used by every app. Add another key, like ⌃ or ⌥.";
            return null;
        }
    }

    [JsonIgnore]
    public bool IsValid => Problem is null;

    /// <summary>The way macOS shows it, in Apple's order: ⌃⌥⇧⌘ then the key.</summary>
    public override string ToString() =>
        (Modifiers.HasFlag(ShortcutModifiers.Control) ? "⌃" : "")
        + (Modifiers.HasFlag(ShortcutModifiers.Option) ? "⌥" : "")
        + (Modifiers.HasFlag(ShortcutModifiers.Shift) ? "⇧" : "")
        + (Modifiers.HasFlag(ShortcutModifiers.Command) ? "⌘" : "")
        + Key;

    /// <summary>Each part on its own, for drawing key caps.</summary>
    public IReadOnlyList<string> Parts()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("⌃");
        if (Modifiers.HasFlag(ShortcutModifiers.Option)) parts.Add("⌥");
        if (Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("⇧");
        if (Modifiers.HasFlag(ShortcutModifiers.Command)) parts.Add("⌘");
        parts.Add(Key);
        return parts;
    }
}
