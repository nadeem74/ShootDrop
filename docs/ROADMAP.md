# Roadmap

Ideas are welcome: open an issue. Items marked **help wanted** are good places to jump in.

## v0.1 (now): safe copier, macOS first ✅

- Card detection by camera profile (DJI Osmo Pocket, Panasonic Lumix, DJI Mic)
- Pick cards, dates and file types; route into your own folder structure
- Copy with xxHash64 verification, `.part` files, never overwrite, copy report
- Saved setups, **Copy like last time**, compact Ready to copy window, menu bar icon
- Smart box with rules (English + Hinglish)
- `shootdrop` CLI on the same engine
- CI on macOS, Linux and Windows; release builds for all four targets

## v0.2: always ready (in progress)

Done: menu bar menu, keep running when closed, start at login, customisable shortcut, auto-open when a card is plugged in,
drag and drop in the folder editor, date picker in Ready to copy, near-zero idle cost ([ADR-006](adr/0006-menu-bar-service.md)).

Still to do:


- **Local AI smart box**: a small offline model for sentences the rules don't cover ([AI.md](AI.md)) — *help wanted*
- Signed and notarised macOS build (Homebrew cask: done in v0.2, via this repo as a tap)
- Linux and Windows: fix what testers find, then mark them supported — *help wanted*
- More camera profiles: Sony, Canon, GoPro, Insta360, Zoom, Rode — *help wanted*

## v0.4: put the cards in, press Return ([ADR-008](adr/0008-autopilot-and-agent.md))

- Ignore drives that aren't camera cards
- Autopilot: auto-open screen with sessions, dated names and one Start; menu bar status; `shootdrop auto`
- Then: tools (also over MCP), the agent with grounded suggestions, traces and evals ([AGENTS-GUIDE.md](AGENTS-GUIDE.md))

## Later (ideas, not promises)

- Clip date from file metadata instead of the file timestamp
- Identify cards by volume UUID instead of name
- Multi-day copies: option for one project per day
- Copy to two drives at once (main + backup)
- MHL checksum reports, the format film crews use
- Import from iPhone over USB (Apple Image Capture) or from an AirDrop inbox
- Upgrade to Avalonia 12

## Not planned

- Editing, transcoding or making proxies
- Cloud upload
- Deleting or formatting cards, ever
