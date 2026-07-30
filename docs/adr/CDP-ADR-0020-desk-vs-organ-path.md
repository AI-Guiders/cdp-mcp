# CDP-ADR-0020: Desk vs organ path (`cdp_cockpit`)

**Status:** accepted  
**Date:** 2026-07-30  
**project-id:** `cdp-mcp` · related CIDE: ADR 0191 / 0193 / 0189  
**Tags:** #cdp #adr #cockpit #soft-organ #context-economy #hang

**Related:** 7890872 desk-pulse for deferred softs · Glass as context economy (0-sync) · CDP-ADR-0018 slim desk

---

## Context

One `BuildAsync` mixed **desk** (thin A / pulse) and **organ dump** (alert / chk / gates body). Agents calling `go=alert|chk` (even after desk-pulse) still hung ~50s+ because deferred soft apply ran a full **glass PublishGlass spray** (~15 channels) plus seat compose on the same turn.

Patch 7890872 kept deferred organs on PlanPulse probes (no git/quality/ResolveSeatOrgan), but did not separate organ work from glass spray. Tool-wake (`cdp_cockpit >20s`) confirmed the hole.

---

## Decision

1. **Desk always thin/cheap** — default `cdp_cockpit` / desk-pulse: seats one-liners, SA pulse, `next[]`. No full pane resolve, no organ-driven glass spray.
2. **Organ = separate path cost** — `go=alert|chk|sys|…` builds organ board (+ latches for SA/ECL/QRH) **without** desk-width work and **without** multi-channel PublishGlass spray on desk-pulse.
3. **`go_detail=full` = organ depth only** — expands `go.result`; never forces desk spray (`seats_detail=full` / `pane_full=` still own W-width).
4. **Full glass spray** remains on the **slow desk path** only (`!WantsDeskPulseFastPath`), when agent explicitly asked for wide desk.
5. Full `BuildAsync` split into separate MCP tool (`cdp_organ`) is a later peel; this ADR ships the contract + thin deferred apply first.

---

## Consequences

- `go=alert` / `go=chk` on pulse desk must return in agent-comfortable time (seconds, not minutes).
- Glass consumers that relied on every soft-organ call refreshing all channels may see stale glass until a desk touch or slow path — acceptable; latch publish for alert/qrh/ecl stays.
- Further peels: organ-only early return (skip seats/nav), then optional `cdp_organ` Meta.

---

## Non-goals (this peel)

New MCP tool; rewriting SoftDispatch/gates; quiet-chrome UI; raising WARN thresholds to hide hang.

---

## Ship

`ApplyDeferredSoftOrgans(..., publishGlassSpray:)` · desk-pulse passes `false` · this ADR
