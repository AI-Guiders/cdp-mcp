# CDP-ADR-0032: Durable background jobs — three layers

**Status:** accepted (phased)  
**Date:** 2026-08-16  
**Context:** MCP CallTool blocks on sync build/test/deploy → host timeout. Hard deploy kills in-proc MCP → Cursor shows green then `Not connected` (stdio zombie, exe often still alive). Long shell jobs (Fremus mirror) die on CDP remount.

## Decision

Three layers, one contract (`lifecycle_job/v0` + `shell_finished` / `build_finished` / `test_finished` / `peer_ship` wake):

### Layer 1 — In-process jobs (CDP MCP) ✅ shipped 0.5.719+

- `cdp_build` / `cdp_test` / `cdp_deploy`: default `background=true` → `IdeLifecycleJobs` enqueue, return `job_id`, auto-arm ignite, notify on finish.
- Poll: `cdp_lifecycle_last`, `cdp_lifecycle_scene`.
- **Limit:** job dies with CDP process (remount, hard deploy KillRunning).

### Layer 2 — Sibling terminal-mcp parity ✅ shipped 0.1.8+

- **Goal:** same `background` + `ignite_arm` on `terminal_run` / `terminal_rerun` when CDP is down or job must outlive CDP.
- **Mechanism:** extract shared **`Cdp.Ignite.Client`** (wake latch + arm store + `Notify(event)`) from `IdeIgniteArmHost` / `IdeIgniteWakeLatch`; reference from `terminal-mcp` + `TerminalMcp.Core`.
- **Wire:** `Program.cs` subscribe `ShellHabitat.Finished` → `shell_finished` notify (background only); auto-arm on `background=true` (mirror `IdeShellIgnite`).
- **DoD:** Fremus mirror started via `terminal_*` survives CDP redeploy; wake fires without manual `cdp_ignite op=arm`.

### Layer 3 — Out-of-process job supervisor ✅ shipped 0.1.0+

- **Goal:** durable queue survives MCP remount, CDP kill, Cursor restart (best-effort).
- **Shape:** lightweight Windows tray / service (`agent-job-supervisor` or `TerminalMcp.Supervisor`) + SSOT `%LocalAppData%/cdp-mcp/jobs/` (jsonl + per-job state).
- **API:** MCP/terminal only `enqueue` + `poll`; supervisor runs dotnet/pwsh/build/deploy, writes result, calls shared ignite `Notify`.
- **DoD:** Fremus mirror + publish-and-deploy enqueue once, complete after full dual-seat rollout without re-arm.

## MCP stability (cross-cutting)

Not a fourth layer — harden transport:

- After hard deploy: expect `Not connected` until remount; use `Recover-CdpSeatRemount.ps1 -Seat cdp` (per-seat nudge, never `-NudgeAllSeats`).
- Survivor seat (`cdp-debug`) stays up during primary rollout.
- Document: green health flash + immediate drop = **CallTool zombie**, not necessarily exe crash (`Get-Process CdpMcp`).

## Phasing

| Phase | Deliverable | Repo |
|-------|-------------|------|
| 1 | IdeLifecycleJobs + shell auto-arm | cdp-mcp ✅ |
| 2a | `Cdp.Ignite.Client` extract | cdp-ignite-client ✅ |
| 2b | terminal-mcp ignite wire | terminal-mcp ✅ |
| 3a | Job store + enqueue protocol | terminal-mcp-core ✅ |
| 3b | Supervisor host + dogfood Fremus/deploy | terminal-mcp-supervisor ✅ |

## References

- ADR 0180 agent shell habitat tabs
- `IdeShellIgnite`, `IdeLifecycleJobs`, `IdeLifecycleIgnite`
- `Recover-CdpSeatRemount.ps1`, kj-1349 per-seat nudge
