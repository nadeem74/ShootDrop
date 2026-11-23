# Product requirements

The living PRD (scope, FR-01 to FR-24, NFRs, AI design, test strategy) is kept as a Claude doc:
https://claude.ai/artifact/RATuW7KDaZbD1HS4uDSExR

Wireframes and the clickable UI flow:
https://claude.ai/artifact/VvN6PxNfrMxYLFku6mwMFj

Before the public launch, export the PRD to Markdown and replace this file with it.

## Requirement status (v0.1 slice in this commit)

| ID | Requirement | Status |
| --- | --- | --- |
| FR-01 | Detect plugged-in volumes, refresh live | Core + app (3 s re-scan) |
| FR-02 | Identify device by profile | Done (Core, JSON profiles) |
| FR-04 | Date filter: day or range | Done (Core, CLI) |
| FR-05 | File-type filter | Done (Core, CLI) |
| FR-06 | Project from template + routing | Done (Core) |
| FR-07 | Unknown card destination | Core + CLI `--route`; app UI pending |
| FR-08 | Progress, cancel, resume | Core + CLI + app |
| FR-09 | Checksum verify (xxHash64) | Done |
| FR-10 | Never overwrite | Done |
| FR-11 | Cards read-only | Done (tested) |
| FR-12 | Remember preferences | settings.json; app UI pending |
| FR-13 | Smart box (rules) | Core + CLI `ask`; app UI pending |
| FR-14 | Copy report | Done |
| FR-15 | CLI | Done |
| FR-17/18 | Folder structure + routing editor | Core model done; editor UI pending |
| FR-19/20 | Saved setups, Copy like last time | Core + CLI; app uses last setup |
| FR-22 | Ready to copy window | First version in app |
| FR-21, 23, 24 | Menu bar, shortcut, background | Not started (v0.2) |
