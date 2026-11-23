# Product requirements

The living PRD (scope, FR-01 to FR-24, NFRs, AI design, test strategy) is kept as a Claude doc:
https://claude.ai/artifact/RATuW7KDaZbD1HS4uDSExR

Wireframes and the clickable UI flow:
https://claude.ai/artifact/VvN6PxNfrMxYLFku6mwMFj

Before the public launch, export the PRD to Markdown and replace this file with it.

## Requirement status (v0.1, 23 Nov 2026)

| ID | Requirement | Status |
| --- | --- | --- |
| FR-01 | Detect plugged-in volumes, refresh live | Done (re-detects when volumes change) |
| FR-02 | Identify device by profile | Done; volumes without DCIM and no profile are hidden |
| FR-03 | Card list with counts and sizes | Done |
| FR-04 | Date filter: day or range | Done |
| FR-05 | File-type filter | Done; extra extensions in Settings |
| FR-06 | Project from template + routing | Done (template folder copied, folders created) |
| FR-07 | Unknown card destination | Done, remembered per card |
| FR-08 | Progress, cancel, resume | Done (resume = re-run; verified files skipped) |
| FR-09 | Checksum verify (xxHash64) | Done |
| FR-10 | Never overwrite | Done |
| FR-11 | Cards read-only | Done (tested) |
| FR-12 | Remember preferences | Done (settings.json) |
| FR-13 | Smart box (rules) | Done; local AI model is v0.2 |
| FR-14 | Copy report | Done |
| FR-15 | CLI | Done |
| FR-17/18 | Folder structure + routing editor | Done |
| FR-19/20 | Saved setups, Copy like last time | Done |
| FR-21 | Menu bar | First cut: icon menu while the app is open |
| FR-22 | Ready to copy window | Done |
| FR-23/24 | Global shortcut, start at login | v0.2 |
| FR-16 | Multi-day range → one project or per day | One project for now |
