# Changelog

## Unreleased

- Homebrew cask: `brew tap nadeem74/shootdrop https://github.com/nadeem74/ShootDrop`, `brew trust nadeem74/shootdrop`, `brew install --cask shootdrop`. Tested on a Mac mini (Apple Silicon, Homebrew 7).
- Buy Me a Coffee link (README and GitHub's Sponsor button).
- README: correct Mac first-launch steps. Since macOS 15, right-click → Open no longer works; use Privacy & Security → Open Anyway.

## 0.2.0 (2026-11-23)

- Runs from the menu bar: cards plugged in, Copy now with your last setup, setups, Settings, Quit
- Closing the window keeps ShootDrop in the menu bar; no Dock icon while it waits
- Start at login (macOS)
- Global shortcut, ⌃⌥⌘C by default, change it in Settings (macOS)
- When cards are plugged in: show Ready to copy, copy automatically, or do nothing
- Drag and drop in the folder editor: move folders, drop a camera on a folder to route it
- Cameras: add, edit and delete cameras inside the app; fill in from a plugged-in card
- File types: remove any extension (with Undo), add your own
- Toast messages with Undo
- New date picker (calendar) in both windows
- One-colour menu bar icon that follows light and dark mode
- Near-zero idle cost: waits for macOS to report new drives instead of checking every 2 seconds

## 0.1.0 (2026-11-23)

First public version. macOS first; Linux and Windows builds are available for testing.

- Card detection with JSON camera profiles: DJI Osmo Pocket, Panasonic Lumix, DJI Mic
- Copy with xxHash64 verification, `.part` files, never overwrite, `copy-report.txt`
- Folder templates and presets, routing per camera and per file type (audio goes to `Audio/` by default)
- Date filters (today, yesterday, last N days, ranges) and file-type filters
- Saved setups, Copy like last time, Ready to copy window, menu bar icon
- Smart box with English and Hinglish rules
- `shootdrop` CLI
- CI on macOS, Linux and Windows; release builds for macOS (arm64, x64), Windows (x64) and Linux (x64)
