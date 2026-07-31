# CDP-ADR-0024: Recall gate — pull → reconcile (self-steer) → align → ready

**Status:** accepted  
**Date:** 2026-07-31  
**project-id:** `cascade-ide` · consumer: CDP / Agent Env  
**Tags:** #cdp #adr #pressure #recall #lifecycle #cockpit #locus

**Related:** CDP-ADR-0018 (pressure desk) · CDP-ADR-0022 (memo line) · lifecycle `recall → explore → …` · cockpit-ease agreement

---

## Context

Lifecycle already starts with **recall**, but it was soft: pull memory, then jump to `act`.
AutoIgnition + last-wins stash can faithfully continue the *wrong* Domain (Avalonia peels while Glass is primary).
Cursor-rule alone does not survive wake as well as a **cockpit-visible status**.

Agreement: difficulties easable by cockpit → ease via cockpit.

---

## Decision

1. Strengthen **recall** as a gate with substatuses on the pressure organ (not a parallel lifecycle, not new `CdpPhase` values):
   - **pull** — `op=recall` (+ `op=line` when Domain/Next contested)
   - **reconcile** — compare memo vs priority **and self-steer** (fix Domain/TM/next; invent/park). Internal locus: decide when SSOT+memo suffice; do not wait for operator to name the slice.
   - **align** — persist corrections (`op=stash` + TM)
   - **ready** — gate green; exit to explore/plan/act
2. Wire on `cdp_pressure`: `op=reconcile|align|ready` (aliases `op=gate to=`). `op=recall` enters **pull**.
3. Pulse/explain/checklist expose `recall·{status}` so cockpit SA/pressure seat shows the gate.
4. Persist `recall_gate` on `PressureDoc` (survives remount with stash).
5. Anti-pattern: «blocked on: operator names X» when konspekt+README/TM already suffice = learned helplessness / external locus.

---

## Consequences

- Wake/L1 path has an explicit place to **decide**, not only restore text.
- SoftOrgan/pressure pulse becomes the continuity desk for locus-of-control practice.
- Later (non-goal v0): hard-block leave-`CdpPhase.Recall` until `ready` via `SessionContext`.

---

## Non-goals (v0)

New top-level `CdpPhase` enum values; affordance seed explosion; auto-LLM reconcile; forcing gate on every casual chat turn (triggers: wake, L1, cold session, Domain/epic invent).

---

## Ship

`IdePressureChannel.Gate` · pulse/explain/checklist · domain card · tests · **0.5.324**
