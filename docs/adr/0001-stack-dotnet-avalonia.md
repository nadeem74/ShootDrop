# ADR-001: .NET 10 + Avalonia for the desktop app

- Status: Accepted
- Date: 2026-11-23
- Decider: Nadeem Ahmed (architect)

## Context

ShootDrop ships on macOS first, and Linux and Windows are planned. It is open source and
part of the architect's public portfolio. The app needs a main window, a compact
"Ready to copy" window and a menu bar icon, plus (in v0.2) start-at-login and a
system-wide keyboard shortcut.

Options considered: native SwiftUI, .NET 10 + Avalonia, Tauri 2 (Rust + Angular), Electron.

## Decision

Build on **.NET 10 (LTS)** with **Avalonia** for the UI.

- One codebase for macOS now and Linux/Windows later.
- C# is the architect's main language, so every pull request can be reviewed in depth.
- Everything that touches footage lives in `ShootDrop.Core`, which has no UI references
  and is tested on macOS and Linux in CI.

## Consequences

- The UI is drawn by Avalonia, not native AppKit controls. Acceptable for a utility app.
- Menu bar: Avalonia's `TrayIcon` shows in the macOS menu bar.
- Start-at-login and the global shortcut need a small macOS-specific module
  (`ShootDrop.Platform.Mac`, v0.2): a LaunchAgent / `SMAppService` call and a
  Carbon `RegisterEventHotKey` binding.
- SwiftUI stays an option for a future native Mac shell over the same core if needed.
