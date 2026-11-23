using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ShootDrop.Platform.Mac;

/// <summary>
/// Shows or hides ShootDrop's Dock icon at run time (NSApplication activation policy). While only the menu bar
/// icon is left, ShootDrop is an "accessory" app: no Dock icon, not in ⌘-Tab. Opening a window makes it a
/// regular app again so the window can come to the front.
/// </summary>
[SupportedOSPlatform("macos")]
public static class DockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const long Regular = 0, Accessory = 1;

    public static void Show()
    {
        var app = SharedApplication();
        if (app == IntPtr.Zero) return;
        objc_msgSend_long(app, sel_registerName("setActivationPolicy:"), Regular);
        objc_msgSend_bool(app, sel_registerName("activateIgnoringOtherApps:"), true);
    }

    public static void Hide()
    {
        var app = SharedApplication();
        if (app != IntPtr.Zero) objc_msgSend_long(app, sel_registerName("setActivationPolicy:"), Accessory);
    }

    private static IntPtr SharedApplication() =>
        objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_long(IntPtr receiver, IntPtr selector, long value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
}
