# Changelog

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
