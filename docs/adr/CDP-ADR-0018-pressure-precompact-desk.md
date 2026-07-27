# CDP-ADR-0018: Pressure desk — L1 pre-compact prep (`cdp_pressure` / `go=pressure_desk`)

**Status:** accepted  
**Date:** 2026-07-27  
**project-id:** `cascade-ide` · consumer: CDP / Agent Env  
**Tags:** #cdp #adr #pressure #compact #continuity #ignite #task-manager #harness

**Related:** playbook-context-pressure-checkpoint-v1 · cockpit/v1.20 · AutoIgnition (`cdp_ignite`) · Task Manager (`go=plan`) · CDP-ADR-0016/0017 (FM)

---

## Context

Cursor injects an agent-only **L1 pressure notify** (~2–3 turns before host summarization). Agent must prepare durable state **without** offering export/checkpoint ritual to the operator (harness rule).

Silent host summary ≠ memory. Lost axes that already bit dogfood: **AutoIgnition re-ARM**, **Task Manager focus**, **work in CDP** (not Cursor host Write).

---

## Decision

1. Soft organ Meta `cdp_pressure` + `go=pressure_desk` (aliases `pressure`, `compact_prep`, `pre_compact`). Seat **P**.
2. Ops: `scene` | `arm` | `stash` | `clear`/`disarm` | `recall`.
3. On L1 notify → `op=arm` → checklist (Ignite / Plan / CDP habitat / invariants) → `op=stash body=`.
4. Durable stash: `%LocalAppData%/cdp-mcp/pressure-stash.json` + `pressure-LATEST.md`.
5. Slim desk: `pressure` pulse when armed; `next[]` elevates `go=pressure`.
6. Does **not** auto-offer export to operator. Export remains on request.

---

## Consequences

- Agent has a cockpit affordance for the L1 window.
- Post-compact: `op=recall` recovers stash independent of platform summary.
- Autonomy loop: stash then **re-ARM** `cdp_ignite` before end turn.

---

## Non-goals (v0)

Host token meter (L0); auto-export transcript; replacing Task Manager / Ignite organs.

---

## Ship

`IdePressureChannel` · wire Program/Cockpit/Seats · cockpit/v1.20 · **0.5.234**
