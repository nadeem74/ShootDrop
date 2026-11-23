using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ShootDrop.Platform.Mac;

/// <summary>
/// A system-wide keyboard shortcut (FR-23) using Carbon's RegisterEventHotKey. Unlike a keyboard event tap,
/// this needs no Accessibility or Input Monitoring permission: macOS only tells us when this exact combination
/// is pressed. The callback arrives on the main thread (the app's run loop).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class GlobalHotKey : IDisposable
{
    // Virtual key codes (Events.h) and Carbon modifier masks.
    public const uint KeyC = 8;
    public const uint Cmd = 1 << 8, Shift = 1 << 9, Option = 1 << 11, Control = 1 << 12;

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint KeyboardClass = 0x6B657962; // 'keyb'
    private const uint HotKeyPressed = 5;          // kEventHotKeyPressed
    private const uint Signature = 0x53447270;     // 'SDrp'

    private static Action? s_pressed;
    private static IntPtr s_handler;

    private IntPtr _hotKey;

    private GlobalHotKey(IntPtr hotKey) => _hotKey = hotKey;

    /// <summary>Registers the shortcut. Returns null if macOS refuses it (for example, another app owns it).</summary>
    public static GlobalHotKey? Register(uint keyCode, uint modifiers, Action pressed)
    {
        s_pressed = pressed;
        var target = GetApplicationEventTarget();
        if (s_handler == IntPtr.Zero)
        {
            var spec = new EventTypeSpec { EventClass = KeyboardClass, EventKind = HotKeyPressed };
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> callback = &OnHotKey;
            if (InstallEventHandler(target, (IntPtr)callback, 1, &spec, IntPtr.Zero, out s_handler) != 0)
                return null;
        }
        var id = new EventHotKeyId { Signature = Signature, Id = 1 };
        return RegisterEventHotKey(keyCode, modifiers, id, target, 0, out var hotKey) == 0 && hotKey != IntPtr.Zero
            ? new GlobalHotKey(hotKey)
            : null;
    }

    public void Dispose()
    {
        if (_hotKey == IntPtr.Zero) return;
        UnregisterEventHotKey(_hotKey);
        _hotKey = IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    private static int OnHotKey(IntPtr callRef, IntPtr evt, IntPtr userData)
    {
        try { s_pressed?.Invoke(); }
        catch (Exception) { /* never let an exception cross into native code */ }
        return 0; // noErr
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint numTypes, EventTypeSpec* list,
        IntPtr userData, out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target,
        uint options, out IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
