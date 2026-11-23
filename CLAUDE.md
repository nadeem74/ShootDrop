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

1. Signed + notarised .dmg via GitHub Actions; push the repo to GitHub (fix commit email first).
2. v0.2: global shortcut + start at login (`ShootDrop.Platform.Mac`), keep running in the menu bar.
3. v0.2: local AI model for the smart box (ADR-005), eval set from RuleParserTests.
4. Clip dates from file metadata instead of last-write time.
5. Card identity by volume UUID instead of name.
6. Multi-day copies: option for one project per day (FR-16).
7. Upgrade to Avalonia 12 when stable for us.

## Links

- PRD: https://claude.ai/artifact/RATuW7KDaZbD1HS4uDSExR
- Wireframes / clickable flow: https://claude.ai/artifact/VvN6PxNfrMxYLFku6mwMFj
