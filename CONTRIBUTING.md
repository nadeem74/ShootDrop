# Contributing to ShootDrop

Thanks for helping. The most useful contributions right now:

1. **Camera profiles** for devices we don't recognise yet (`profiles/*.json`). Include the folder
   layout of a real card (file and folder names only, no footage) in the pull request.
2. **Bug reports** with your OS, card type and the `copy-report.txt` from the project.

## Ground rules

- Footage safety comes first. A change must never write to, rename or delete anything on a source
  card, and never overwrite a file in a project (see `docs/adr/0003-architecture.md`).
- Every pull request includes tests. Copy behaviour is tested with fake cards (`tests/.../FakeCard.cs`).
- Copy logic belongs in `ShootDrop.Core`; the app and CLI only call it.
- `dotnet build` and `dotnet test` must pass on macOS and Linux (CI runs both).

## Workflow

- Branch from `main`, keep pull requests small, reference the requirement ID (e.g. FR-09) or issue.
- Bigger design changes start as an ADR in `docs/adr/` so the reasoning stays public.
