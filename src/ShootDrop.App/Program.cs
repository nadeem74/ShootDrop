using Avalonia;
using Avalonia.Controls;

namespace ShootDrop.App;

internal static class Program
{
    /// <summary>Started by the login item: go straight to the menu bar, no window.</summary>
    public static bool StartHidden { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartHidden = args.Contains("--background");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
