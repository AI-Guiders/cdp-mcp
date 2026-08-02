# Domain card: Teeth (guest-host pulse)

- id: `teeth`
- organ: `cdp_teeth` / `IdeTeethChannel` + `IdeTeethTape`
- product: `#CDP`

## Invariants

- Pulse embeds on `cdp_health` (`teeth_pulse`) — must not lie with `cdt=?` when CDT is reachable.
- Auto-sample CDT when `LastCdtUp` unknown or note older than ~15s (`ShouldRefreshCdtSample`); `cdt=true` forces live sample.
- `submit_kind=stop|queue` during wake fire = wait-idle busy (normal), not CDT-down.
- Remount Not-connected while exe alive → `Recover-CdpSeatRemount.ps1` via `terminal_*` (kill+nudge); human Reload last.

## Entry

- `go=teeth` / `cdp_teeth` · `IdeTeethChannel.PulseLine` · `Recover-CdpSeatRemount.ps1`
- QRH: `remount-after-deploy`

## Antipatterns

- Digging "CDT broken" from `cdt=?` without `go=teeth cdt=true` / waiting for auto-refresh (pre-0.5.498 PulseLine never sampled).
- Treating Composer Stop wait-idle as CDT failure.
- Human Reload first when kill+nudge would remount the zombie seat.

## last_ship

- 0.5.498: teeth PulseLine/scene/explain auto-refresh CDT when unknown/stale (~15s); Recover-CdpSeatRemount.ps1 + QRH/health recovery_note · 2026-08-02
