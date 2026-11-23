# ADR-003: One core library, thin front ends

- Status: Accepted
- Date: 2026-11-23

## Decision

```
src/
  ShootDrop.Core   no UI, no OS-specific UI code. Profiles, scanning, date rules,
                   routing, planning, copy + verify, settings, smart-box rules.
  ShootDrop.Cli    command-line front end on the same core (scripts, scheduled runs)
  ShootDrop.App    Avalonia desktop app (Ready window first, then full window)
tests/
  ShootDrop.Core.Tests   xUnit, fake cards built in temp folders
profiles/          camera profiles as JSON, embedded into Core at build time
```

Rules:

1. The core never writes to, renames or deletes anything on a source card. Sources are
   opened with `FileAccess.Read`.
2. A file counts as copied only after the copy is re-read and its checksum matches the source.
3. Files are written as `<name>.part` and renamed when verified, so a half-copied file
   never looks finished.
4. Nothing in a project folder is ever overwritten. Identical files are skipped; a
   different file with the same name gets a new name.
5. Front ends hold no copy logic. If the CLI and the app both need it, it belongs in Core.

## Consequences

- The CLI gives an end-to-end test path on real cards before the UI exists.
- New cameras are added with a JSON file, not code.
