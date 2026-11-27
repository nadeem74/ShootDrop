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
