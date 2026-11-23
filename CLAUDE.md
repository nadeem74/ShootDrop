# ShootDrop: notes for Claude Code

ShootDrop copies footage from camera cards into a project folder, verified by checksum.
Owner and architect: Nadeem Ahmed. He reviews every change; explain decisions in plain English.

## Status

Builds on macOS (Apple Silicon, .NET SDK 10.0.404) with 0 warnings, and all 24 tests pass (23 Nov 2026).
`Setup ShootDrop.command` re-runs build + tests and writes setup.log / build.log / test.log.
`Check cards.command` lists cards and does a dry run (nothing copied) into cards.log.
`Build and open ShootDrop.command` builds artifacts/ShootDrop.app (self-contained) and opens it.

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
- `src/ShootDrop.App`: Avalonia app. Today: the compact "Ready to copy" window only.
- `tests/ShootDrop.Core.Tests`: xUnit with fake cards in temp folders (`FakeCard.cs`).
- `profiles/*.json`: camera profiles, embedded into Core.
- `docs/adr`: decisions. `docs/PRD.md`: links to the PRD and wireframes, requirement status.

## Rules that must never break (ADR-003)

1. Never write to, rename or delete anything on a source card. Open sources read-only.
2. A file is "copied" only after the written copy is re-read and its xxHash64 matches.
3. Write `<name>.part`, rename when verified. Delete `.part` on failure or cancel.
4. Never overwrite in a project: identical → skip, different → new name.
5. Copy logic lives in Core only. Every Core change comes with a test.
6. Never run a real copy against Nadeem's actual cards or SSD without asking him first.
   For manual tests use `--source <folder>` with fake cards.

## Conventions

- .NET 10, C# latest, nullable on, file-scoped namespaces, central package versions in `Directory.Packages.props`.
- Small PRs; title starts with the requirement ID (e.g. "FR-17: folder editor"). Update `docs/PRD.md` status.
- New architecture choices → new ADR in `docs/adr`.

## Backlog (in order)

1. Done: builds and tests pass on macOS. Still to check: upgrade to Avalonia 12.
2. Run the CLI against fake cards, then (with Nadeem) against his real cards: `plan` first, then `copy`.
3. App: Settings screen (projects folder, template folder) instead of the CLI `config` step.
4. App: full window (cards, dates, file types, smart box), matching the wireframes.
5. App: unknown card → folder dropdown, remembered per card (FR-07).
6. App: folder structure editor (FR-17/18) and saved setups (FR-19).
7. Performance: replace the 3 s full re-scan with mount/unmount events + a cheap count; scan only on change.
8. Card identity by volume UUID instead of name (`DetectedCard.CardId`).
9. v0.2: menu bar (Avalonia TrayIcon), global shortcut + login item (`ShootDrop.Platform.Mac`), local AI smart box.
10. Packaging: signed + notarised .dmg via GitHub Actions.

## Links

- PRD: https://claude.ai/artifact/RATuW7KDaZbD1HS4uDSExR
- Wireframes / clickable flow: https://claude.ai/artifact/VvN6PxNfrMxYLFku6mwMFj
