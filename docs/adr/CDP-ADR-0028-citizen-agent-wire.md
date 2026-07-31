# CDP-ADR-0028: Citizen agent wire (pulse frames)

**Status:** accepted (wire contract + parser; **host injection path open**)  
**Date:** 2026-07-31  
**Tags:** #cdp #adr #citizen #wire #dark-cockpit

**Related:** [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md) · fixtures `docs/design/citizen-wire-fixtures/` · `CitizenWireParser` · CDP-ADR-0020 (desk vs organ) · CDP-ADR-0025 (isolation) · CDP-ADR-0026 (ai-keys) · CIDE ADR 0021 (W/C/A)

**Supersedes as normative:** the design draft remains the long form; this ADR locks the decision.

---

## Context

Guest Cursor→MCP burns context on JSON walls and blind peer. Citizen habitat must speak **desk as afferent frames**, not `CallTool(cdp_cockpit)` as the default look.

Peels 1–2 and 4–5 shipped before this ADR (fixtures, parser, dual-seat pressure, AiKeys loader). Peel 3 (persona in system prompt) waits on completions host. Peel 6 is this promotion.

---

## Decision

1. **Citizen wire** = Dark Cockpit attention (W·C·A) + ADR-0020 cost paths + peer duplex, carried as **pulse frames** (`@frame` / `@intent` / `@event`), not MCP JSON in the face.
2. **Canonical long form** stays [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md). Fixture transcripts under `docs/design/citizen-wire-fixtures/` are the dogfood corpus.
3. **Parser** (`CitizenWireParser`) is in-tree and tested; guest Cursor does **not** inject frames until a host path exists.
4. **Host path (open):** when in-habitat chat/completions land, habitat injects desk/peer frames and may use the draft citizen system prompt from the design doc. Until then: bridge table in the design doc (guest approximates via `cdp_cockpit` / Autoi crutches).
5. **Do not invent a second SSOT** — wire projects the same desk semantics as Dark Cockpit + ADR-0020.

---

## Consequences

- Normative pointer for citizen host work is this ADR + design doc + fixtures.
- Persona peel (#3) remains blocked on host; inventing a fake completions loop is still out of bounds.
- Self-steered Next peels after this ADR: host injection sketch, or persona wiring once host exists — agent may invent *adjacent* peels that do not require a fake host.

---

## Verification

- Fixtures parse under `CitizenWireParser` tests (shipped 0.5.328).
- Design doc Next peels 1–2, 4–5 marked done; 6 → this ADR; 3 open.
