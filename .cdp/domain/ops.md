# Domain card: ops / dual-seat

- id: `ops`
- organ: `IdeOpsPulse` (+ `IdeDeploy` seat roots)
- product: `#CDP`

## Invariants

- Seat: `cdp` = `D:\cdp-mcp`, `cdp-debug` = `D:\cdp-mcp-debug`.
- `ops_pulse` / `cdp_health.seats`: self_version · sibling_version · lag (ProductVersion short).
- Hard deploy defaults to sibling — survivor seat stays old until remount/soft-self.
- TM WitDB is seat-local (`StateRoot/{seat}/intent-workspace.witdb`) — sibling does not open primary's file.

## Entry

- `cdp_health` — seats + ops_pulse
- `go=deploy` — hard sibling; remount target for new bits
- **Citizen peer path (0.5.569):** `@intent deploy …` host-executes `IdeDeploy.Run`. `go=deploy*` still place-only.

## Antipatterns

- Shell FileVersionInfo on both installs as first dig.
- Hard-deploy self from inside `cdp_shell_*`.
- Manual `Stop-Process` of same-seat pile while live MCP is among them (prefer remount after `IdeSeatProcessReclaim`; skip via `CDP_SKIP_SEAT_RECLAIM=1` only for intentional multi).
- Immediate KillRunning on every `Not connected` when process may still be healthy — prefer `Recover-CdpSeatRemount.ps1 -SoftFirst` (nudge only), escalate to kill if still dead.

## last_ship

- **2026-08-05** — FDR cockpit 50ms lock: live WitDB torn (`pageNumber` OOR) → failed Open leaks FileShare.None → quarantine Move failed → heal never landed. Fix: GC settle + MoveWithRetry on torn quarantine. Dig: AdoNet open probe.
- **2026-08-05** — FDR dig: dual-seat root tape + remount kill tax → seat FDR tape · reclaim sleep 800ms · WitDB remount backoff · Recover `-SoftFirst`
- **0.5.626** — `cdp_health` default `detail=pulse`: skip LSP `resolved_probe` path resolve + compact JSON; `detail=full|lsp` = prior fat card. Why: every health CallTool paid Resolve×presets and looked "all MCP slow". Lived: agent ops dig.
- 0.5.569: citizen `@intent deploy` → `IdeDeploy.Run` (peer remount without Cursor) · 2026-08-03
- same-seat remount reclaim: `IdeSeatProcessReclaim.Ensure` kills older same-exe `CdpMcp` on startup (sibling path untouched) @ 0.5.450 · 2026-08-02
- per-seat WitDB isolation dogfood: `cdp_open` store under `…/cdp/intent-workspace.witdb`; kill same-seat zombie pile before diagnose @ 0.5.448 · 2026-08-02
- dual-seat version pulse @ 0.5.410 · 2026-08-01
