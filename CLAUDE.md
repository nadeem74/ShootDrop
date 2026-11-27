# ShootDrop: guide for AI coding agents

This file is read by AI coding agents (Claude Code and similar) working in this repo. Humans: it's also
a good one-page summary. ShootDrop copies footage from camera cards into a project folder, verified by checksum.
Maintainer and architect: Nadeem Ahmed. He reviews every change; explain decisions in plain English.

## Status

v0.1. Builds with 0 warnings; tests pass on macOS, Linux and Windows in CI. macOS is used daily on real cards;
Linux and Windows build in CI and need real-card testing.

macOS helper scripts (double-click in Finder):
- `scripts/macos/setup.command`: installs .NET 10 if missing, builds, tests (setup.log / build.log / test.log).
- `scripts/macos/check-cards.command`: lists cards and does a dry run (nothing copied) into cards.log.
- `scripts/macos/build-app.command`: builds artifacts/ShootDrop.app (self-contained) and opens it.
- `scripts/macos/package-app.sh`: wraps a publish folder into a .app (used by the release workflow too).

## Commands

```bash
dotnet build ShootDrop.slnx
dotnet test ShootDrop.slnx
dotnet run --project src/ShootDrop.App
dotnet run --project src/ShootDrop.Cli -- help
```

## Layout

- `src/ShootDrop.Core`: all logic, no UI. Cards (profiles, detection, scanning), Media (FileKind, DateRule),
  Projects (FolderTemplate, Routing), Copying (planner, engine, report), Settings, SmartBox (rule parser),
  `ShootDropService` (the one entry point for front ends).
- `src/ShootDrop.Cli`: `shootdrop` command, thin.
- `src/ShootDrop.Platform.Mac`: macOS-only interop (global shortcut, login item, Dock icon). ADR-006.
- `src/ShootDrop.App`: Avalonia app. `Services/BackgroundController` runs the menu bar, shortcut and card arrival. ShellViewModel hosts pages: Welcome, Ready, Main, Review, Copying, Done, Settings.
- `tests/ShootDrop.Core.Tests`: xUnit with fake cards in temp folders (`FakeCard.cs`).
- `profiles/*.json`: camera profiles, embedded into Core.
- `docs/adr`: decisions. `docs/PRD.md`: requirements and status. `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/AI.md`, `docs/AGENTS-GUIDE.md` (agent plan, ADR-008).

## Rules that must never break (ADR-003)

1. Never write to, rename or delete anything on a source card. Open sources read-only.
2. A file is "copied" only after the written copy is re-read and its xxHash64 matches.
3. Write `<name>.part`, rename when verified. Delete `.part` on failure or cancel.
4. Never overwrite in a project: identical → skip, different → new name.
5. Copy logic lives in Core only. Every Core change comes with a test.
6. Never run a real copy against the user's actual cards or drives without asking first.
   For manual tests use `--source <folder>` with fake cards.

## Conventions

- .NET 10, C# latest, nullable on, file-scoped namespaces, central package versions in `Directory.Packages.props`.
- Small PRs; title starts with the requirement ID (e.g. "FR-17: folder editor"). Update `docs/PRD.md` status.
- New architecture choices → new ADR in `docs/adr`.

## Backlog

See `docs/ROADMAP.md`. Next: global shortcut + start at login (`ShootDrop.Platform.Mac`), local AI smart box
(ADR-005, `docs/AI.md`), signed + notarised macOS build, Linux/Windows fixes from testers.

## Releasing

Update `CHANGELOG.md`, then push a tag `vX.Y.Z`. `.github/workflows/release.yml` builds four downloads (macOS Apple Silicon and Intel .dmg,
Windows .exe, Linux .tar.gz) and attaches only those to the GitHub Release.
