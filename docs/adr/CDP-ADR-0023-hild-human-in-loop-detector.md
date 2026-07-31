# CDP-ADR-0023: HILD — Human-in-the-loop detector (CDT Composer)

**Status:** accepted  
**Date:** 2026-07-31  
**project-id:** `cascade-ide` · consumer: CDP / Agent Env  
**Tags:** #cdp #adr #ignite #cdt #hild #continuity

**Related:** AutoIgnition · Intercom Voice Cannon · Autonomous Continuity Contract

---

## Context

CDT is already online for AutoIgnition. When the Composer submit button is Voice (empty) the operator may be about to type — or already away. Without a detector, autonomy either waits forever or wakes on coarse timers.

Operator design (Intercom):
- Primary signal = **Composer text** (more reliable than mic/Voice alone).
- Idle interval = **5s** (not 30).
- Edge → status `human_away` → AutoIgnition wakes PF.

---

## Decision

1. Always-on HILD watch (default ARMED) polls CDT Composer once per second.
2. Pure FSM `IdeHildDetector`: Voice/empty + no text for 5s → edge `human_away` **once per spell**; Composer text / Send resets; Stop/Queue ends the spell.
3. On edge: `Notify(human_away)` for `when=human_away` arms + seed minimal AutoI wake (Intercom cannon pattern).
4. Suppress seed wake while `await_operator` latch is active.
5. **Once-latch:** after edge, no re-fire until Composer text/Send (human returned). Agent Stop→Voice must not thrash.
6. After HILD wake with no human exchange: continuity **1–2s** (or take task immediately) — not 45m idle timer.
7. Ops: `cdp_ignite op=hild|hild_on|hild_off`; scene includes `hild` slice.

---

## Consequences

- Autonomy resumes shortly after the human leaves an empty Composer.
- Aggressive 5s may wake while the operator is still reading — accepted by PM for v0.

---

## Non-goals (v0)

Mic/dictation as presence; Glass Intercom typing as presence; tunable idle without rebuild.

---

## Ship

`IdeHildDetector` · `IdeIgniteArmHost.Hild` · Meta/domain · **0.5.320** · cooldown **0.5.321**
