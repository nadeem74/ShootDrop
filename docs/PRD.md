# Product requirements (v0.1)

## Problem

Copying a shoot day off several cards into a clean project folder is manual and easy to get wrong.
Tools that do it well are paid and aimed at film crews. ShootDrop started as a macOS bash script that
was hard-coded to one drive and one set of cameras, copied only "today", checked files by size only,
and had no way to choose file types.

**Vision:** a free, open-source app (macOS first, Linux and Windows next) that copies the right footage
from every card into a project folder you define, checks every byte, understands plain requests like
"all cards, yesterday", and runs fully offline, AI included.

## Users

- **Primary:** solo creators and vloggers with two to four devices (e.g. DJI Osmo Pocket, a mirrorless camera, a wireless mic).
- **Secondary:** small crews, wedding and event shooters, podcasters with audio recorders.
- **Contributors:** developers adding a profile for their own gear.

## Scenarios

| ID | Scenario | What the user does |
| --- | --- | --- |
| S1 | End of a shoot day | Plugs in all cards, presses Copy now |
| S2 | Forgot to copy yesterday | Types "all cards, yesterday" |
| S3 | Three-day trip | Picks a date range |
| S4 | One device only | Types "Pocket only, 21 Nov" or ticks one card |
| S5 | Unknown camera | Picks its folder once; it's remembered |
| S6 | Copy stopped halfway | Runs again; verified files are skipped |
| S7 | Needs photos too | Ticks Photos; the choice is remembered |

## Functional requirements

| ID | Requirement | Status |
| --- | --- | --- |
| FR-01 | Detect plugged-in volumes and refresh when cards change | ✅ |
| FR-02 | Identify each card's device through profiles | ✅ |
| FR-03 | Card list with file counts and sizes for the current filter | ✅ |
| FR-04 | Date filter: one day or a range | ✅ (calendar in both windows, v0.2) |
| FR-05 | File types: video, audio, photo, proxy, plus custom extensions | ✅ |
| FR-06 | Project created from a template folder; each profile routes to a subfolder | ✅ |
| FR-07 | Unknown card: choose its folder, remembered per card | ✅ |
| FR-08 | Progress, cancel, resume | ✅ (resume = run again) |
| FR-09 | Verify every file by xxHash64 | ✅ |
| FR-10 | Never overwrite | ✅ |
| FR-11 | Cards opened read-only | ✅ |
| FR-12 | Remember preferences | ✅ |
| FR-13 | Smart box: text → filter, shown in the controls before Copy | ✅ rules · v0.2 local AI |
| FR-14 | Copy report inside the project | ✅ |
| FR-15 | Command-line mode on the same engine | ✅ |
| FR-16 | Multi-day range → one project, or one per day | One project for now |
| FR-17 | Folder structure editor with presets and template import | ✅ (drag and drop, v0.2) |
| FR-18 | Routing table per camera and per file type (incl. audio, "Don't copy") | ✅ (drag a camera onto a folder, v0.2) |
| FR-19 | Saved setups | ✅ |
| FR-20 | One-click "Copy like last time" | ✅ |
| FR-21 | Menu bar item | ✅ v0.2 |
| FR-22 | Compact "Ready to copy" landing window | ✅ |
| FR-23 | Global keyboard shortcut | ✅ v0.2, customisable (macOS) |
| FR-24 | Start at login, run in the background | ✅ v0.2 (macOS) |

## Non-functional requirements

| Area | Requirement |
| --- | --- |
| Data safety | Outranks everything. Sources read-only; `.part` then rename after checksum match. |
| Speed | The card reader sets the pace. Hash while copying so each source is read once for the copy. |
| Platforms | v0.1: macOS on Apple Silicon. Core tested on Linux and Windows in CI. |
| AI | Fully offline, optional download, under 500 MB, under 2 s per answer on an 8 GB laptop. |
| Privacy | No telemetry. |
| Distribution | Release builds from CI; signed and notarised macOS build planned. |

## Test strategy

Tests are written with each change and run in CI on every push (shift-left), not in a phase at the end.

- **Unit:** date and smart-box parsing, profile detection, routing, name collisions, settings upgrades.
- **Integration:** the copy engine against fake cards (temp folders with real DCIM layouts and set timestamps).
- **Manual hardware runs before a release:** DJI Osmo Pocket 4P, Lumix S9, DJI Mic on macOS.
- **Riskiest cases first:** card pulled mid-copy, full destination drive, same file name from two cameras,
  clips across midnight, exFAT timestamps, files over 4 GB, Unicode project names, long Windows paths.

## Non-goals

Editing, transcoding, proxies, cloud upload, deleting or formatting cards.
