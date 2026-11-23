# Contributing to ShootDrop

Thanks for being here! 🙌 Whether you write C#, own a camera nobody else has, or just run Linux,
there's something useful you can do. You don't need permission: fork it, try something, open a PR.

## Most wanted right now

1. **Linux and Windows testers.** ShootDrop builds on both in CI, but nobody has run it against real
   camera cards there yet. Download the build from [Releases](https://github.com/nadeem74/ShootDrop/releases),
   plug in a card, try a copy into a test folder, and fill in a
   [platform test report](https://github.com/nadeem74/ShootDrop/issues/new?template=platform_test.yml).
   "It worked" is a useful report too.
2. **Camera profiles** (`profiles/*.json`) for gear we don't recognise yet. Include the folder layout of a
   real card in the PR (file and folder names only, no footage). `tree -L 3 /Volumes/YOUR_CARD` is perfect.
3. **The local AI smart box** (v0.2). Start with the eval set described in [docs/AI.md](docs/AI.md).
4. **Bugs** with your OS, card type and the `copy-report.txt` from the project.

## Set up

```bash
git clone https://github.com/<you>/ShootDrop.git
cd ShootDrop
dotnet build ShootDrop.slnx
dotnet test ShootDrop.slnx
dotnet run --project src/ShootDrop.App
```

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Any editor works: Rider, VS Code with C# Dev Kit, or Visual Studio.

Testing a copy without real cards: `dotnet run --project src/ShootDrop.Cli -- plan --source <folder-that-looks-like-a-card>`.

## Ground rules

- **Footage safety comes first.** A change must never write to, rename or delete anything on a source card,
  and never overwrite a file in a project. See [ADR-003](docs/adr/0003-architecture.md).
- **Copy logic belongs in `ShootDrop.Core`.** The app and CLI only call it.
- **Core changes come with tests.** Copy behaviour is tested with fake cards (`tests/ShootDrop.Core.Tests/FakeCard.cs`).
- `dotnet build` (0 warnings) and `dotnet test` must pass. CI checks macOS, Linux and Windows.
- Bigger design changes start as an ADR in `docs/adr/`, so the reasoning stays public.

## Pull requests

- Branch from `main`, keep PRs small and focused.
- Reference the requirement ID (e.g. `FR-23`) or issue in the title.
- Fill in the PR template; screenshots for UI changes are great.
- AI-assisted code is fine. You're responsible for understanding and testing what you submit.

## Be kind

We follow a simple [Code of Conduct](CODE_OF_CONDUCT.md). Be respectful, assume good intent, help newcomers.
