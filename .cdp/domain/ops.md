# Domain card: ops / dual-seat

- id: `ops`
- organ: `IdeOpsPulse` (+ `IdeDeploy` seat roots)
- product: `#CDP`

## Invariants

- Seat: `cdp` = `D:\cdp-mcp`, `cdp-debug` = `D:\cdp-mcp-debug`.
- `ops_pulse` / `cdp_health.seats`: self_version · sibling_version · lag (ProductVersion short).
- Hard deploy defaults to sibling — survivor seat stays old until remount/soft-self.

## Entry

- `cdp_health` — seats + ops_pulse
- `go=deploy` — hard sibling; remount target for new bits

## Antipatterns

- Shell FileVersionInfo on both installs as first dig.
- Hard-deploy self from inside `cdp_shell_*`.

## last_ship

- dual-seat version pulse @ 0.5.410 · 2026-08-01
