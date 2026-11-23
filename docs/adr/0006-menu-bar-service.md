# ADR-006: Running from the menu bar (v0.2)

- Status: Accepted
- Date: 2026-11-23
- Decider: Nadeem Ahmed (architect)

## Context

v0.2 makes ShootDrop a quiet background helper: always in the menu bar, a global shortcut, start at login,
and (optionally) acting when a card is plugged in. It must cost close to nothing while idle and must never
start a copy the user didn't expect.

## Decision

- **Menu bar menu: a native menu, not a custom popover.** `TrayIcon` + `NativeMenu` gives a real macOS menu
  (keyboard, VoiceOver, dark mode) with no extra native code. The trade-off: no text box, so Copy now uses
  today's date as the project name and "Copy now with a different name…" opens the Ready window.
  On macOS Avalonia can't swap a tray icon's menu object, so one `NativeMenu` is kept and its items replaced.
- **Hide instead of quit.** `ShutdownMode.OnExplicitShutdown`; closing the window hides it. The Dock icon is
  hidden while only the menu bar icon is left (NSApplication activation policy via `objc_msgSend`).
- **Start at login: a per-user LaunchAgent** (`~/Library/LaunchAgents/io.github.shootdrop.app.plist`, starts
  with `--background`). `SMAppService` is the modern API but needs a signed app; the LaunchAgent works for
  unsigned community builds and is easy to inspect. Revisit once builds are signed and notarised.
- **Global shortcut: Carbon `RegisterEventHotKey`.** Needs no Accessibility / Input Monitoring permission,
  unlike an event tap. Customisable; the `Shortcut` rules (needs ⌃/⌥/⌘, refuses ⌘Q-style keys) live in Core.
  A new shortcut is saved only if macOS accepts it, so "another app owns it" is reported, not silently broken.
- **Card watching: notifications, not polling.** A `FileSystemWatcher` on `/Volumes` (`/media`, `/run/media`
  on Linux) plus a 15 s safety check replaces the 2 s poll. Windows keeps a 3 s check.
- **When cards are plugged in: decided in Core** (`CardArrival`, tested). Default is to show Ready to copy.
  Copy automatically only when every card is a known camera and there is something to copy; never while a copy
  runs; never for cards already plugged in when ShootDrop starts. Otherwise fall back to the Ready window.
- macOS-only interop lives in `src/ShootDrop.Platform.Mac`, marked `[SupportedOSPlatform("macos")]`;
  callers check `OperatingSystem.IsMacOS()`, so the app still builds and runs on Windows and Linux.

## Consequences

- Idle cost is a sleeping process (~60–100 MB of memory, no CPU) until a card appears.
- The menu can't hold a text field; a custom popover window can come later if it's missed.
- Windows/Linux get the tray menu and hide-on-close; shortcut and start-at-login are macOS-only for now.
