namespace ShootDrop.Core.Background;

/// <summary>What ShootDrop does by itself when new cards are plugged in (FR-21).</summary>
public enum CardArrivalAction
{
    /// <summary>Open the small "Ready to copy" window. The default: one click, nothing happens without you.</summary>
    ShowReadyWindow,

    /// <summary>Start copying straight away with the last setup, if it is safe to (see <see cref="CardArrival"/>).</summary>
    CopyAutomatically,

    /// <summary>Only update the menu bar menu.</summary>
    DoNothing,
}

/// <summary>Running from the menu bar (FR-21, FR-23, FR-24).</summary>
public sealed class BackgroundSettings
{
    /// <summary>Closing the window hides it; ShootDrop keeps running in the menu bar.</summary>
    public bool KeepRunningInMenuBar { get; set; } = true;

    /// <summary>Start ShootDrop (hidden, in the menu bar) when the user logs in.</summary>
    public bool StartAtLogin { get; set; }

    /// <summary>The system-wide shortcut ⌃⌥⌘C opens Ready to copy.</summary>
    public bool ShortcutEnabled { get; set; } = true;

    public CardArrivalAction WhenCardsPluggedIn { get; set; } = CardArrivalAction.ShowReadyWindow;
}
