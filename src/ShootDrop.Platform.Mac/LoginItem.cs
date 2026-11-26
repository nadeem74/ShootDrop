using System.Runtime.Versioning;
using System.Security;

namespace ShootDrop.Platform.Mac;

/// <summary>
/// Start at login (FR-24) with a per-user LaunchAgent: ~/Library/LaunchAgents/io.github.shootdrop.app.plist.
/// It works for unsigned builds (SMAppService needs a signed app) and is easy to inspect or delete by hand.
/// The agent starts ShootDrop with --background, so it goes straight to the menu bar. If ShootDrop crashes,
/// macOS starts it again (KeepAlive on unsuccessful exit); choosing Quit ends it until the next login.
/// </summary>
[SupportedOSPlatform("macos")]
public static class LoginItem
{
    public const string Label = "io.github.shootdrop.app";
    public const string BackgroundArgument = "--background";

    public static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public static bool IsEnabled => File.Exists(PlistPath);

    /// <summary>Points the login item at <paramref name="executable"/> (the running app). Safe to call again after the app moved.</summary>
    public static void Enable(string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, Plist(executable));
    }

    public static void Disable()
    {
        if (File.Exists(PlistPath)) File.Delete(PlistPath);
    }

    internal static string Plist(string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executable)}</string>
            <string>{BackgroundArgument}</string>
          </array>
          <key>RunAtLoad</key><true/>
          <key>KeepAlive</key>
          <dict>
            <key>SuccessfulExit</key><false/>
          </dict>
          <key>ThrottleInterval</key><integer>30</integer>
          <key>ProcessType</key><string>Interactive</string>
          <key>LimitLoadToSessionType</key><string>Aqua</string>
        </dict>
        </plist>
        """;
}
