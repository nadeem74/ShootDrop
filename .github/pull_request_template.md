## What and why

<!-- What does this change, and which issue or requirement (e.g. FR-23) does it address? -->

## How I tested it

<!-- Tests added, OS used, cards tried. Screenshots for UI changes. -->

## Checklist

- [ ] Never writes to, renames or deletes anything on a source card
- [ ] Never overwrites a file in a project
- [ ] Copy logic is in `ShootDrop.Core`, with tests
- [ ] `dotnet build` has 0 warnings and `dotnet test` passes
