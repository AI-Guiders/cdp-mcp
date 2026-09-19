# CDP-ADR-0231: Same-wake ground soft recalibration

**Status:** Accepted  
**Date:** 2026-09-19  
**project-id:** `cdp-mcp`  
**Tags:** #cdp #adr #ground #same-wake #axb #just-culture

**Related:** CDP-ADR-0020 desk vs organ · CDP-ADR-0024 recall gate · Habitat AxB Attractors L5

---

## Context

Wake continuity is inter-wake (pressure stash, ignite latch). Inside one wake, agents still thrash: buffer-mill, mutate-before-recall, early act without inventory. Lectures and hard blocks fail Just Culture.

## Decision

Ship thin organ `cdp_ground` / `go=ground`:

1. Pulse JSON `{pattern, next, resume_ok}` — `buffer_mill | ignore_hint | early_act | biped | ok`.
2. Process-static `IdeSameWakeLatch` counts buffer ops + recall + one ignore nudge.
3. Desk `next[]` surfaces ground+inventory when buffer_mill hot.
4. Full-wake first mutate without recall → soft `ground` card on edit result (not refuse).
5. Awareness card `.cdp/rules/awareness-same-wake.md` (5–7 patterns).
6. Equal-standing one-liner in `ChargeOwnershipPostfix`.

## Anti

Long lecture postfix; hard block v1; blame language.

## Verification

`SameWakeGroundTests` — pulse patterns, buffer_mill desk next, ignore nudge once.
