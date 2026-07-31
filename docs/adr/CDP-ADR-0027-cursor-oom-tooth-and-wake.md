# Cursor guest-host OOM tooth + OOM Wake

- **Status:** Accepted
- **Date:** 2026-07-31
- **Related:** CDP-ADR-0025 (citizen/guest isolation) · AutoIgnition ConnectionWatch · RemountInitialized wake

## Context

Cursor Electron can terminate with `reason: 'oom'` ("The window terminated unexpectedly"). That is a **guest-host** failure — not Glass/CIDE. Agent thread dies; CDT `:9222` drops; continuity must recover without blaming our projectors.

## Decision

1. **Tooth (Win32):** detect OOM terminate dialog → click **New Window** (same native dialog plane as stall Keep Waiting).
2. **OOM Wake (AutoI):** always-on `StartOomWatch` (Program, like HILD): CDT `/json/version` down→up after `MinDown` → schedule one-shot `oom-wake-*` timer (`charge_mode=oom`) with recall/amnesia charge.
3. System wake arms are not superseded by continuity timer re-arm (same class as remount/tool wakes).

## Consequences

- Operator still may need to reattach workspace; wake tells agent to `cdp_pressure op=recall`.
- False positives: brief CDT blips under MinDown ignored; WakeCooldown limits thrash.
- Does not prevent Cursor OOM — recovers the loop after it.
