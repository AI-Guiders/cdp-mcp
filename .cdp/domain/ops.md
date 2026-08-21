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
- `-Target debug` (relative) → lands in repo `cdp-mcp\debug\`, not seat `D:\cdp-mcp-debug` — health still lag; use absolute `D:\cdp-mcp-debug` or `cdp_deploy target=sibling|debug`.
- Manual `Stop-Process` of same-seat pile while live MCP is among them (prefer remount after `IdeSeatProcessReclaim`; skip via `CDP_SKIP_SEAT_RECLAIM=1` only for intentional multi).
- Immediate KillRunning on every `Not connected` when process may still be healthy — prefer `Recover-CdpSeatRemount.ps1 -SoftFirst` (nudge only), escalate to kill if still dead.
- **Green flash then `Not connected` after deploy** — often Cursor MCP stdio zombie while `CdpMcp.exe` still runs; `Recover-CdpSeatRemount.ps1 -Seat cdp` (per-seat nudge). Use **cdp-debug** survivor during primary hard deploy.
- Running `pwsh -File CdpReloadNudge.ps1 -Server cdp` expecting a bump — pre-entry the file was library-only (silent no-op). Use `-File` after entry ship, or `.` + `Invoke-CdpReloadNudge`.

## last_ship

- **2026-08-21 cdp_deploy mode=apply** — promote staged `.next` → live without republish: stop CdpService, robocopy mirror, clear `cdp-pending-update.json`, restart + bridge nudge. `mode=soft` then `mode=apply` for tool-only ships. Commit 368c2ca.
- **2026-08-21 Open-stack WitDB pin 14.0.1** — `OutWit.Database.EntityFramework` **12.8.0 → 14.0.1** (nuget.org latest); aligned CDP + Forge + Cascade. EF Relational 10.0.10 transitive unchanged. `dotnet build` CDP green. — `deploy-20260816041304-a9de7e` idle ok 7.2s · debug→sibling · `worker_exe_path`+`ignite_seat` · both **0.5.725** lag=false · `peer_ship` CDT invoked (teeth 04:13:13). Worker `WaitForArmDeliveryAsync` before exit.
- **2026-08-16 ADR-0032 L3c durable deploy smoke GREEN** — `deploy-20260816040847-197a58` idle ok · debug→sibling hard 8.7s · `worker_exe_path`+`ignite_seat` stamped · both seats **0.5.724** lag=false. Gap: worker exited before async `peer_ship` CDT → **0.5.725** `WaitForArmDeliveryAsync`. Supervisor `TryNotifyIgniteViaCdp` on failure (0.5.724).
- **2026-08-16 ADR-0032 L3c RID-aware durable lifecycle** — `WorkerExePath` stamped at enqueue; supervisor resolves `CdpMcp`/`CdpMcp.exe` + Install-Cdp roots (linux/mac/win). Supervisor multi-RID publish. 0.5.721+.
- **2026-08-16 ADR-0032 L2+L3 durable jobs** — Layer 1: `IdeLifecycleJobs` in-proc (0.5.719+). Layer 2: `Cdp.Ignite.Client` + terminal-mcp `ignite_arm` (v0.1.9+). Layer 3: `DurableJobStore` + `TerminalMcp.Supervisor.exe --watch` — `terminal_run durable=true` → `%LocalAppData%/cdp-mcp/jobs/`; poll `terminal_job_last`; survives remount. Deploy `D:\terminal-mcp\`.
- **2026-08-16 Lifecycle background jobs** — `cdp_build` / `cdp_test` / `cdp_deploy` default `background=true`: enqueue in-process job, MCP returns immediately, auto-arm `build_finished|test_finished|peer_ship`, wake on finish. Poll `cdp_lifecycle_last kind=…`. `wait=true` or `background=false` = sync (MCP may timeout). `IdeLifecycleJobs` + `IdeLifecycleIgnite`. — `Install-Cdp.ps1` auto-RID (`win-x64`/`linux-x64`/`osx-arm64`/`osx-x64`), unix roots + `CdpMcp` binary + chmod; `release.yml` matrix packages all four RIDs. SoftFL REJECT invent linux-arm64 until RID shipped.
- **2026-08-14 GitHub-only newcomer install** — GitLab Generic Packages retired. `Install-Cdp.ps1` downloads from `AI-Guiders/cdp-mcp` Releases; `publish-gitlab-package.ps1` deleted; Actions `release.yml` + `ci-checkout-siblings.ps1` clone only `AI-Guiders/*` (7 former open-tree siblings published). Live: https://github.com/AI-Guiders/cdp-mcp/releases/tag/v0.5.715. Secret: `GH_PAT` for private `ai-native-ui`.
- **2026-08-08 Dig SoftFL-safe residual expand CLOSED** — dig=`ops.md`+cascade-ide remotes · lived SoftFL-safe: `origin` push URL prefers SSH → timeout `193.124.113.7:22` · escape `git push github develop` (HTTPS `AI-Guiders/cascade-ide`) shipped SoftFL tip `db8fd77a` · stamp tip for Autoi: prefer `github` remote when origin SSH dies · SoftFL invent REJECT remount thrash. Evidence dig=remote -v + push log.
- **2026-08-06 Dig Flight durable SoftFirst DIG REJECT** — dig-lived: SoftFirst Recover + flight-durable remount survive already SHIPPED/VERIFY (ops SoftFirst dual-seat dogfood · glass flight-durable 2026-08-05 · CdpReloadNudge -File entry). Cabin Glass **pid=37464** still up since 11:58 through SoftFirst remounts this epic · health 0.5.675 lag=false. Reopen SoftFirst invent = thrash chrome. Evidence `cascade-ide/tmp-glass-shots/softfirst-flight-digreject-20260806-1630.png` + `cdp_see`. SoftFL REJECT.
- **2026-08-06 CdpReloadNudge -File entry** — lived: mid-turn Not connected after Recover; `pwsh -File CdpReloadNudge.ps1 -Server cdp` did nothing (functions only) until `.` + `Invoke-`. Entry now bumps named seat. SoftFL REJECT.
- **2026-08-06b dotsource gate** — Path-equality aborted Recover (empty Server + exit); `InvocationName -ne '.'` + SoftFirst Recover dogfood.
- **2026-08-06 dual-seat real hard 0.5.675** — lived: `-Target debug` seeming ship left `D:\cdp-mcp-debug` on **0.5.674** · hard absolute Target → lag=false self+sib **0.5.675** · wave FullReady-dualagent-sibling · SoftFL REJECT.
- **2026-08-05 SoftFirst dual-seat dogfood** — mid-turn dual `Not connected` with both exes alive (debug=50876 kept; primary remounted 5344→31708 by Cursor nudge). Recover `-SoftFirst` ×2 (no agent KillRunning / no NudgeAllSeats). Health GREEN 0.5.667 both · cabin Glass pid=57780 survived. DIG REJECT invent thrash chrome — path already shipped; wave flight-durable = soak verify.
- **2026-08-05** — OutWit.Database.EntityFramework **12.2.0** (was 1.0.3); author confirmed tear/leak fixes. Stay on WitDB seat files.
- **2026-08-05** — FDR cockpit 50ms lock: live WitDB torn (`pageNumber` OOR) → failed Open leaks FileShare.None → quarantine Move failed → heal never landed. Fix: GC settle + MoveWithRetry on torn quarantine. Dig: AdoNet open probe.
- **2026-08-05** — FDR dig: dual-seat root tape + remount kill tax → seat FDR tape · reclaim sleep 800ms · WitDB remount backoff · Recover `-SoftFirst`
- **0.5.626** — `cdp_health` default `detail=pulse`: skip LSP `resolved_probe` path resolve + compact JSON; `detail=full|lsp` = prior fat card. Why: every health CallTool paid Resolve×presets and looked "all MCP slow". Lived: agent ops dig.
- 0.5.569: citizen `@intent deploy` → `IdeDeploy.Run` (peer remount without Cursor) · 2026-08-03
- same-seat remount reclaim: `IdeSeatProcessReclaim.Ensure` kills older same-exe `CdpMcp` on startup (sibling path untouched) @ 0.5.450 · 2026-08-02
- per-seat WitDB isolation dogfood: `cdp_open` store under `…/cdp/intent-workspace.witdb`; kill same-seat zombie pile before diagnose @ 0.5.448 · 2026-08-02
- dual-seat version pulse @ 0.5.410 · 2026-08-01
