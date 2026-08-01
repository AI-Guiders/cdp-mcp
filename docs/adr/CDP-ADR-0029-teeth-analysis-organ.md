# Teeth analysis organ (guest-host afferent)

- **Status:** Accepted
- **Date:** 2026-08-01
- **Related:** CDP-ADR-0027 (OOM tooth + wake) · CDP-ADR-0023 (HILD) · FDR (tool-call black box — separate)

## Context

Guest-host picture (CDT/Stop, remount·oom delivery, OOM tooth, partner away) was spread across `cdp_ignite` list/probe, arms JSON, and rare FDR wake lines. Dogfood: remount arm `firing` + Composer submit=Stop looked like a silent miss.

## Decision

1. Soft organ `cdp_teeth` / `go=teeth` — scene|tail|explain; cheap default without CDT attach (`cdt=true` opt-in).
2. Append-only `teeth-tape.jsonl` under `%LocalAppData%/cdp-mcp` — wake lifecycle + tooth + CDT edges + partner away/here/escalate. Not mixed with FDR tool-call tape.
3. `teeth_pulse` on `cdp_health` next to `ops_pulse`.
4. Partner: first HILD away = status + schedule escalate (~60s); still away → `SetAutonomous(true)`.

## Consequences

- Agent one-glance after hard deploy / OOM without digging list+probe+arms.
- Delivery harden (do not drop once without send_ok) stays a follow-on leaf — see first, then heal.
