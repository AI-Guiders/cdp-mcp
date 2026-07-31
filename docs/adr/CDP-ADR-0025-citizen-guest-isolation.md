# CDP-ADR-0025: Citizen vs guest isolation (guest must not thrash citizen)

**Status:** accepted (foundation; dual-seat ignite/HILD + pressure seat dirs shipped 0.5.330; citizen host not shipped)  
**Date:** 2026-07-31  
**Tags:** #cdp #adr #citizen #guest #isolation #continuity

**Related:** [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md) · CDP-ADR-0021 (Glass projector) · CDP-ADR-0023 (HILD) · CDP-ADR-0024 (recall gate) · CIDE ADR 0199 (workspace isolation)

---

## Context

Today agents run as **guests**: Cursor harness → MCP plug → CDP organs.  
Target **citizen**: completions host inside CDP habitat (frames/intents; desk as afferent).

Both will coexist for a long time. Guest Autoi/HILD/remount/wake can already thrash continuity (wrong Domain, double wake, OOM on Cursor host ≠ our Glass). When citizen ships, an unsupervised guest must **not** own habitat peer events or steal the loop.

---

## Decision

### 1. Roles

| Role | Owns | Does not own |
|------|------|----------------|
| **Citizen** | Habitat loop, peer generation, desk frames, pressure/recall for *this* seat, in-habitat Autoi (if any) | Cursor CDT Autoi into guest Composer |
| **Guest** | MCP CallTool escape; optional secondary look at desk | Remount of citizen runtime; citizen Autoi fire; silent Domain invent that overrides citizen TM |
| **Operator Glass** | Projector (latches → pixels) | Agent loop (not an agent) |

### 2. Isolation rules (v0 — design contract)

1. **One loop owner per seat.** `peer` / generation / remount events are seat-scoped. Guest wake must not reset citizen peer gen.
2. **Guest Autoi is adapter-only** (CDT into Cursor). Citizen continuity uses habitat organs (pressure/ignite seat store), never Cursor Composer inject as spine.
3. **Shared SSOT (desk, latches, TM WitDB) is projectable by both** — mutate only via gated organs. Guest host Write / PathMutateGate bypass remains a integrity violation for both.
4. **HILD / human_away** on guest Composer must not fire citizen wake. Separate latch stores / seat ids (`ignite-arms-cdp.json` vs guest chat id).
5. **Recall gate** (ADR-0024) applies to whichever agent is acting; citizen and guest may have separate pressure docs under workspace isolation (ADR 0199 roots).
6. Until citizen host exists: document guest as **temporary approximation**; do not grow guest Autoi into permanent spine.

### 3. Wire

Citizen frames carry required `peer=` (see citizen-agent-wire-v0). Guest may omit peer until bridge maps remount → `@event peer`.

---

## Consequences

- Product checklist before citizen ship: dual-seat ignite (**ready**), dual pressure state roots (**ready** — `StateRoot/{seat}/` stash+memo; TM WitDB stays workspace-shared), Glass remains projector-only.
- Operator “Window terminated” / Cursor OOM are **guest-host** failures — do not blame Glass/citizen without evidence.

## Non-goals

- Shipping completions host in this ADR.
- Killing guest MCP (escape hatch stays).
- Hard process sandbox (OS) — later AOS line.
