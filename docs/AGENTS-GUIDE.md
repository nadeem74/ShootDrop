# Building an agent into ShootDrop: a learning guide

This guide grows with the work. Each phase adds a section: what we built, the idea behind it in plain English,
and where to find it in the code. Read it top to bottom and you can explain the whole thing in an interview.
The decision itself is in [ADR-008](adr/0008-autopilot-and-agent.md).

## The big picture

```text
Card inserted ─► Autopilot finds new clips ─► ONE screen: sessions + names + space ─► [Start] ─► copy ─► verify ─► notify
                                                 ▲
                                    later: the agent fills this screen in better
                                    (names from calendar/GPS, warnings, summary)
```

## Phase 0: the ideas

### 1. Automation vs. agent

- **Automation** follows fixed steps. Same input, same output, every time. Example: "copy every clip newer than the
  last copy, then verify it". You test it once and trust it forever.
- **An agent** is a model in a loop: look → think → use a tool → look at the result → repeat → stop. It is useful where
  the right answer depends on judgement, like "is this one shoot or two?" or "what is this shoot called?".

Rule of thumb: **if you can write the rule down, it is automation.** Use the model only for what you can't write down.
That is why Phase 1 has no AI at all.

### 2. Human in the loop

The tool does all the work, then asks the person once before acting. In ShootDrop that is the one screen with
**Start copying**. Trust grows by watching it be right; then the person can switch to *Countdown* or *Fully automatic*.
Who may change that setting matters: the person can, the agent can't.

### 3. Tools

An agent can't click buttons. It gets a short list of **tools**: a function with a name, a one-line description and an
input format (JSON schema). The model reads the descriptions and picks which to call. Example:

```json
{ "name": "check_storage",
  "description": "Is the projects drive connected, and is there room for this many bytes?",
  "input": { "bytesNeeded": "number" } }
```

**Least privilege**: give the agent only what the job needs. Most ShootDrop tools only read. The one that acts,
`start_copy`, accepts only a preview that has already passed the safety checks.

### 4. Guardrails live in code, not in the prompt

Telling a model "never delete files" in the prompt is a wish. Not *giving* it a delete tool, and having the copy engine
refuse to overwrite anything, is a guarantee. ShootDrop's safety rules (ADR-003) sit in the engine, so even a confused
model can at worst choose a bad folder name.

### 5. Grounding

Models can make things up. The fix: every answer must point to real data. "Sinhagad Fort, from your calendar event at
9:00" is grounded. "Sinhagad Fort" with no source is not allowed; the dated name `2026-11-27 0915` stays.

### 6. One brain, many front ends

The menu bar, the auto-open screen and the CLI all call the same `AutoCopy` code in Core. Bugs get fixed once, and the
three can't drift apart. ShootDrop already works this way through `ShootDropService`.

### 7. Traces and evals (coming in Phase 4)

- **Trace**: a short log of what the agent looked at, which tools it called and why it chose what it chose.
- **Evals**: a fixed set of fake shoots with the right answers, run on every change. Because a model doesn't give the same
  answer every time, you measure "how often is it right?" instead of "does this one test pass?".

## Phases

| Phase | What | You learn |
| --- | --- | --- |
| 0 | Plan: ADR-008, this guide, PRD entries | Automation vs agent, guardrails, grounding |
| 1 | Autopilot: ignore non-card drives, auto-open screen with one Start, menu bar status, `shootdrop auto` | Event-driven flows, doing things safely twice (idempotency) |
| 2 | Tools, also offered over MCP | Tool calling, schemas, least privilege |
| 3 | The agent loop, behind a model interface (local model or Claude API) | Agent loop, prompts, when to stop and ask |
| 4 | Decision traces and evals in CI | Testing non-deterministic systems |

## Words you'll hear

- **Agent loop:** look → think → act with a tool → read result → repeat until done.
- **Tool / function calling:** the model asks the app to run a named function with JSON input.
- **MCP (Model Context Protocol):** a standard way to offer tools to any AI app (Claude Desktop, IDEs).
- **Idempotent:** doing it twice has the same effect as once. Inserting the same card again copies nothing new.
- **Grounding:** answers tied to real data, with the source shown.
- **Human in the loop:** a person approves before the action happens.
- **Eval:** a scored test set for AI behaviour.

---

## Phase 1: the autopilot (built)

**What happens:** a card goes in → `CardArrival` decides `ShowAutopilot` → the app opens `AutoPage` →
`AutoCopy.Propose` finds the new clips and groups them into sessions → you press **Start** → the normal copy engine runs
→ `AutoCopy.Remember` stores, per card, the newest clip now safely copied.

| File | What it does |
| --- | --- |
| `src/ShootDrop.Core/Autopilot/AutoCopy.cs` | The one shared flow: `Propose` (what's new, sessions, date names), `Preview` (the copy plan for the sessions you kept), `Remember` (the per-card bookmark) |
| `src/ShootDrop.App/ViewModels/AutoPageViewModel.cs` + `Views/Pages/AutoPage.axaml` | The one screen: sessions with tick boxes and names, the space check in red if it's a problem, Start |
| `src/ShootDrop.Cli/AutoCommand.cs` | `shootdrop auto`: the same flow in a terminal |
| `src/ShootDrop.Core/Cards/ProfileDetector.cs` | A card counts as an audio recorder only if (almost) everything on it is audio, so a backup drive doesn't pop the screen up |

**Ideas to take away**

- **"What's new" is a bookmark, not a guess.** Per card, the newest clip already copied. Anything later is new.
  A card it has never seen offers only its latest day, so an old full card doesn't offer months of footage.
  Unticking a session and copying a later one moves the bookmark past it: unticked means "not wanted".
- **Idempotent by design.** Names come from clip times, so the same session always maps to the same folder, and the
  engine skips files that are already there and identical. Inserting the same card twice copies nothing twice.
- **The request remembers a rule, not a date.** A copy saves its request as "last used". The autopilot keeps your
  setup's rule ("today") there, so the Ready window doesn't get stuck on a past date.

## Phase 2: tools (built)

| File | What it does |
| --- | --- |
| `src/ShootDrop.Core/Agent/ShootDropTools.cs` | Four tools with JSON schemas: `list_cards`, `find_new_sessions`, `check_storage` (read-only) and `copy_sessions` |
| `src/ShootDrop.Cli/McpServer.cs` | `shootdrop mcp`: speaks MCP (JSON-RPC 2.0, one message per line on stdin/stdout) so Claude Desktop can use those tools |

**Try it:** add this to Claude Desktop's MCP settings, then ask "any new footage on my cards?":

```json
{ "mcpServers": { "shootdrop": { "command": "/opt/homebrew/bin/shootdrop", "args": ["mcp"] } } }
```

(Use wherever your `shootdrop` CLI is; build it with `dotnet publish src/ShootDrop.Cli`.)

**Ideas to take away**

- A **tool** is just a function plus a description and an input schema. The model reads the descriptions to decide.
- **MCP** is the plug standard: write the tools once, any AI app can use them.
- `copy_sessions` doesn't get any new powers. It calls the same autopilot and engine, so ADR-003 still holds, and
  Claude Desktop asks you before each call.

## Phase 3: the naming agent (built)

| File | What it does |
| --- | --- |
| `src/ShootDrop.Core/Agent/SessionNamer.cs` | **Rules first:** splits your note ("morning X, evening Y", "X then Y", "X aur phir Y") into names and matches them to sessions in order. Also `Grounding`: the check every name must pass |
| `src/ShootDrop.Core/Agent/NamingAgent.cs` | **The agent loop** (look → tool → result → repeat → `submit_names`), the `IModelClient` interface, and `AnthropicModel` (Claude over plain HTTP) |

**How a run goes**

```text
You: "Sinhagad Fort in the morning, cafe later"     Sessions: 09:15, 14:05, 18:30
Claude → get_sessions, get_note, get_past_projects
Claude → submit_names: 0 "Sinhagad Fort", 1 "Golden hour magic", 2 "Cafe review"
Grounding: 0 ✓ in your note · 1 ✗ refused (made up) → keeps "2026-11-23 1405" · 2 ✓ existing project
```

That exact case is a test: `AutopilotTests.The_agent_uses_tools_and_made_up_names_are_refused`.

**Turning Claude on (optional):** put your API key in a file `anthropic-key.txt` next to ShootDrop's settings
(macOS: `~/Library/Application Support/ShootDrop/`), or set `ANTHROPIC_API_KEY` for the CLI. Without a key the rules do
the naming. The model is `AgentModel` in settings (default `claude-haiku-5-5`, the small and cheap one).

**Ideas to take away**

- **Least privilege:** the naming agent gets three read-only tools and one way to hand in its answer. It has no copy tool at all.
- **Guardrails in code:** "never invent a name" is in the prompt, but `Grounding.Enforce` is what makes it true.
- **A loop needs a stop:** at most 6 turns. If the model never hands in names, nothing changes.
- **Swap the brain:** the agent only knows `IModelClient`. A local model is one new class; the tests use a scripted fake.

## Phase 4: traces and tests (built)

- **Trace:** every naming run writes `last-naming.txt` next to the settings: which tools it called, which names it
  accepted and why, which it refused. The CLI prints the same lines.
- **Tests without a network:** `ScriptedModel` in `AutopilotTests.cs` plays back fixed model turns. That tests the loop
  and the guardrails exactly, every time, for free.
- **Not yet:** scoring the real model on a set of sample shoots (an eval). That needs an API key in CI and costs a
  little per run, so it waits until naming has more facts to work with (calendar, GPS).

## What's next

- Facts from your **calendar** (event names and times) and **GPS** in the clips, so names can be grounded without a note.
- **Countdown** and **Fully automatic** trust levels, and a notification with *Show in Finder* / *Rename*.
- A **local model** behind `IModelClient`, then compare it with Claude on the same test set.
