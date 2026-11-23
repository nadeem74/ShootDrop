# ShootDrop

**Copy a shoot day off your camera cards in one click, with every file verified.**

ShootDrop finds the cards you plug in (DJI Osmo Pocket, Lumix, DJI Mic, and more by profile),
picks the footage recorded on the days you choose, and copies it into a new project folder
built from your own folder structure. Cards are only ever read. Nothing in a project is
overwritten. Every file is checked with a checksum after it's copied.

> Status: early development (v0.1, macOS first). Linux and Windows are planned.

## Why

Creators shooting on several devices copy the same way every day: plug in cards, make a
project folder, sort clips into `Video/Pocket 4P`, `Video/Camera`, `Audio/DJI Mic`, and hope nothing
got corrupted. Paid tools do this for film crews. ShootDrop is the free, open-source
version for solo creators and small teams.

## Features (v0.1)

- Detects cards and the device they came from, using JSON camera profiles
- Pick cards, a day or a date range, and file types (video, audio, photos, proxies)
- Your folder structure: copy your own template folder, or use a preset (My template, A-roll / B-roll)
- Saved setups and **Copy like last time**
- Smart box: type "all cards, yesterday for Sinhagad trip"
- Copy + xxHash64 verify, resume-safe (`.part` files), copy report in every project
- `shootdrop` command-line tool on the same engine

Planned: menu bar quick copy, global shortcut, start at login, a small local AI model for the
smart box, Linux and Windows builds.

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build ShootDrop.slnx
dotnet test ShootDrop.slnx
dotnet run --project src/ShootDrop.App          # the app
dotnet run --project src/ShootDrop.Cli -- help  # the CLI
```

## Try the CLI

```bash
# where projects go (saved to ~/Library/Application Support/ShootDrop/settings.json)
dotnet run --project src/ShootDrop.Cli -- config --root "/Volumes/SSD 1TB/SSD Video Projects"

dotnet run --project src/ShootDrop.Cli -- cards
dotnet run --project src/ShootDrop.Cli -- plan --date yesterday --name "Sinhagad trip"
dotnet run --project src/ShootDrop.Cli -- copy --date yesterday --name "Sinhagad trip"
dotnet run --project src/ShootDrop.Cli -- ask "Pocket only, last 3 days"
```

## Add your camera

Add a JSON file to `profiles/` (or to `~/Library/Application Support/ShootDrop/profiles/`):

```json
{
  "id": "gopro-hero",
  "name": "GoPro HERO",
  "defaultFolder": "Video/GoPro",
  "match": { "dcimFolderContains": ["GOPRO"] }
}
```

Pull requests with new profiles are very welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## How it's built

- .NET 10, Avalonia UI, xUnit. Decisions are recorded in [docs/adr](docs/adr).
- `ShootDrop.Core` holds all copy logic and has no UI code; the app and the CLI are thin front ends.

## License

MIT, © 2026 Nadeem Ahmed
