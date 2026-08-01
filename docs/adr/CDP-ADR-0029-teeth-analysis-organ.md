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
4. Partner: first HILD away = status + schedule escalate (~60s); still away → `SetAutonomous(true)` **and** one-shot wake `hild-escalate-away` (`charge_mode=escalate`, composer lead `reason=escalate`). Claim `AwayEscalateDone` under one `HildGate` lock (no TOCTOU storm); stable arm ids `hild-away` / `hild-escalate-away`. **Cross-process** claim file `%LocalAppData%/cdp-mcp/hild-away-claim.json` + named Mutex (same pattern as Intercom cannon) — zombie remounts / N× `CdpMcp.exe` must not N-schedule wakes. Soft delivery: CDT `became_stop` after insert counts as send_ok (`ok_soft_stop`) so once-arms do not thrash. Autonomy latch alone is not a wake — agent must receive Composer charge if the first away turn already ended.

## Consequences

- Agent one-glance after hard deploy / OOM without digging list+probe+arms.
- Delivery harden (do not drop once without send_ok) stays a follow-on leaf — see first, then heal.
