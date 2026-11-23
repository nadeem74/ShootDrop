# ADR-005: Smart box = rules first, local model later

- Status: Accepted (model choice deferred to v0.2)
- Date: 2026-11-23

## Decision

- v0.1: a deterministic rule parser (`ShootDrop.Core.SmartBox.RuleParser`) understands
  dates ("today", "yesterday", "last 3 days", "22 Nov", Hinglish "aaj" / "kal"), card names,
  file types ("audio only", "with photos") and a project name ("for Sinhagad trip").
- v0.2: phrases the rules miss go to a small local model via LLamaSharp (llama.cpp, GGUF),
  constrained to a JSON schema, then validated. The model only fills in the filter; the user
  always confirms before copying.
- Candidate models and the fine-tuning plan are in the PRD (AI design section).

## Consequences

- v0.1 has no model download and no extra native dependencies.
- The rule parser's test cases become the first part of the AI eval set.
