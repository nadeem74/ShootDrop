# ADR-007: Resume interrupted copies with a copy journal

**Status:** accepted (v0.3)

## Context

Copies get cut short: a card is pulled out, the laptop loses power, the app is closed. Until now the advice was
"run the same copy again". That works (finished files are skipped as identical), but the person has to remember the
exact choices, a time split can't easily be rebuilt, every finished file is read again to compare checksums, and a
`.part` left by a power cut made that one file fail.

## Decision

While copying, ShootDrop writes a journal, `unfinished-copy.jsonl`, next to its settings:

- **Line 1:** what the copy is: setup, project name, time split, projects, and every file with its source and destination.
- **One line per finished file**, appended and flushed to disk as it finishes: destination, final path, checksum.
- A line cut off by a power cut is ignored, so at worst one file is copied again.
- The journal is deleted when every file of the copy is in its project.

When a journal exists, the window shows "A copy didn't finish … Resume / Forget".
Resume (`ShootDropService.PrepareResume`) plans the files that aren't finished (or whose copy has since gone),
to the **same destinations**, and runs them with the normal engine. Files whose card isn't plugged in are left out
and counted, and the journal stays until they're copied too. Reports list every file of the whole copy.

The engine now removes a stale `<name>.part` before writing that file. That's ShootDrop's own half-written temp file
in the project, never a finished copy and never on a card, so ADR-003 still holds.

## Consequences

- Resume needs no choices from the person and skips finished files without reading them again.
- Starting a new copy replaces an old unfinished one; the banner makes that visible first.
- The journal holds file paths only, no file contents, and stays on the computer.
