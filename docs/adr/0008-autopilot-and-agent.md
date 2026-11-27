# ADR-008: Autopilot first, then an agent

**Status:** accepted (plan for v0.4), model choice still open

## Context

Today a copy takes a few decisions and clicks: open the app, pick cards and dates, name the project, press Copy.
After a shoot that is the last thing anyone wants to do. The goal is: put the cards in, answer one question, walk away.

"Make it agentic" can mean two very different things, so this record separates them:

- **Autopilot:** fixed steps that must behave the same way every time: find what's new, check the drive and space,
  copy, verify, report. Plain code, fully tested. No AI.
- **Agent:** a model that *decides* things a person decides today, such as project names, where one shoot ends and the
  next begins, and what to do when something is wrong. It works by calling a small set of tools and reading the results.

Until now [AI.md](../AI.md) promised that AI never starts a copy: the person always presses Copy. This record keeps the
spirit of that promise (the person confirms) and changes how much they have to do.

## Decision

1. **Autopilot comes first (Phase 1), with one question.** When a card is inserted, a single screen opens by itself:
   the recording sessions in time order, each with a ready name, and the free space on the drive. One button: **Start
   copying**. After that everything is hands-off: copy, verify, report, a notification with *Show in Finder* and *Rename*.
2. **Default names are date + time of the first clip** of each session, e.g. `2026-11-27 0915` (no `:`, which is unsafe
   in folder names). Renaming later is safe because only the project folder changes, never the card.
3. **One shared flow in Core** (`AutoCopy`), used by every front end: card inserted, menu bar *Copy from cards…*, and
   `shootdrop auto` in the CLI (asks in the terminal, or `--yes`). They cannot behave differently.
4. **Ignore drives that are not camera cards** before auto-open ships, so a backup drive never pops the screen up.
5. **The agent comes later and only through tools** (Phase 2–3): `list_cards`, `find_new_clips`, `suggest_sessions`,
   `check_storage`, `preview_copy`, `start_copy`, `notify`. Read-only tools first; `start_copy` accepts only a preview that
   has already passed the storage and safety checks. The same tools can be offered over MCP so other AI apps can drive ShootDrop.
6. **Every suggestion carries its source** ("from your calendar event at 9:00", "GPS near Sinhagad + past project").
   No source → the agent may not use it, and the date name stays. This is grounding, the guard against made-up answers.
7. **Trust levels**, chosen by the person: *Ask* (default: the one screen above), *Countdown* (starts after 15 s unless
   cancelled), *Fully automatic*. The level is a setting, never something the agent can change.
8. **Safety lives in the engine, not the model.** ADR-003 is untouched: cards read-only, xxHash64 verification, `.part`
   files, never overwrite, never delete. A wrong answer from a model can at worst pick a bad folder name.
9. **Every agent run writes a short decision trace** the person can read, and a fixed set of fake shoots with expected
   answers (evals) runs in CI, so changes that make the agent worse are caught.

## Update, 27 Nov 2026

The maintainer chose to always be asked for project names: the screen shows how many shoots were found and asks for
each name (blank → date name). Countdown / Fully automatic, calendar and GPS naming are dropped. Item 7 is therefore
reduced to *Ask* only.

## Open question

Where the model runs: a small local model (offline, free, private; the AI.md plan) or the Claude API (better reasoning,
needs internet, an API key and a small cost per shoot). The agent will talk to a provider interface so either can be
plugged in. Decide before Phase 3.

## Rejected

- **Ask a chain of questions** (cards? dates? name?). That is the current app with extra steps.
- **Let the model pick names freely.** Creative but untraceable; a wrong name is worse than a dated one.
- **Agent first, autopilot later.** The daily win comes from the autopilot; the agent improves a flow that already works.

## Consequences

- The common case becomes: card in → Return → done.
- AI.md and the README must say the person confirms once, instead of "presses Copy".
- New requirements FR-26 (autopilot) and FR-27 (agent) in the PRD.
