# CDP-ADR-0203: Bridge deploy-gap survival

**Status:** accepted  
**Date:** 2026-08-22  
**Extends:** ADR-0198 (bridge), ADR-0032 (durable jobs)

## Problem

Pre-bridge habitat taught agents a manual procedure when `cdp_deploy mode=apply|hard` kills CdpService:

1. Enqueue durable deploy
2. Expect `Not connected` on poll
3. Wait 15–30s, retry `cdp_lifecycle_last` / `cdp_health`
4. Never shell-escape to `publish-and-deploy.ps1`

After ADR-0198 the **bridge survives** deploy, but it was still a dumb forwarder:

- `WithEnsureRetryAsync` — only 2 attempts / ~15s
- No read of `%LocalAppData%/cdp-mcp/jobs/` when service is down
- Ensurer could race supervisor by auto-starting CdpService mid-apply
- Agents panicked on first `Not connected` and escaped to terminal

## Decision

Bridge owns **transport-level deploy gap survival** (orchestration SSOT: `Cdp.Deploy` / `IdeDeploy` in C# — not `publish-and-deploy.ps1`).

### 0. C# deploy SSOT (2026-09)

- **`Cdp.Deploy`** — planner + orchestrator (soft/hard/apply), distinct service/bridge roots, post-promote `CdpService.exe` + `/healthz` gate.
- **`IdeDeploy`** — MCP/durable worker entry; `engine: "cdp.deploy/csharp"`.
- **`publish-and-deploy.ps1`** — thin escape hatch → `CdpMcp.exe --deploy-cli <payload.json>`.
- **Deprecated:** `-Target` remapping service≈bridge (use explicit `ServiceTarget` + `BridgeTarget` in planner).

### 1. Deploy waiter (default for service-killing modes)

`cdp_deploy` with `mode=apply|hard|rollout` (and not `dry_run` / `bridge_wait=false`):

1. Forward enqueue to CdpService with `background=true`, `durable=true` (strip `wait` / `bridge_wait`)
2. Hold MCP `CallTool` open
3. Poll `DurableJobStore` until job terminal
4. Poll `/healthz` until service ready (post-job ensurer allowed)
5. Return merged JSON with `bridge_wait` envelope + optional `post_deploy_health`

Opt-out: `bridge_wait=false`. Explicit `wait=true` means bridge wait (not in-proc sync).

### 2. Lifecycle local fallback

When HTTP to CdpService fails:

- `cdp_lifecycle_last` / `cdp_lifecycle_scene` → read `DurableJobStore` directly, annotate `bridge_local: true`
- `cdp_health` during in-flight deploy → `detail=bridge_deploy_gap` card (not hard error)

### 3. Ensurer guard

`CdpBridgeServiceEnsurer` must **not** auto-start CdpService while `DurableJobStore.TryGetInFlightKind("deploy")` is set — supervisor owns restart.

Cold boot (no deploy job) behavior unchanged.

### 5. Bridge-local worker fallback

When CdpService HTTP is unavailable at deploy enqueue (connection refused / socket error):

1. Bridge spawns `CdpService.exe` or `CdpMcp.exe` with `--deploy-cli <payload.json>` (same C# SSOT).
2. Holds `CallTool` until worker stdout JSON + optional `/healthz` poll (same as §1 waiter).
3. Annotates `bridge_local: true` — no shell escape to PS1.

`CdpBridgeDeployRunner` + `CdpBridgeInvokeRouter.DeployViaLocalWorkerAsync`.

### 5. Timings (env overrides)

| Env | Default |
|-----|---------|
| `CDP_BRIDGE_DEPLOY_WAIT_MS` | 180000 |
| `CDP_BRIDGE_DEPLOY_POLL_MS` | 500 |
| `CDP_BRIDGE_DEPLOY_GAP_RETRY_MS` | 750 |

## Agent habitat (deprecated)

- ~~Poll lifecycle after Not connected on apply~~
- ~~Shell escape to `publish-and-deploy.ps1` when MCP drops~~
- ~~`-Target` for dual-instance deploy (collides service/bridge publish roots)~~

Still valid:

- Survivor seat for rollout orchestration (CdpService policy)
- `Recover-CdpSeatRemount.ps1` for Cursor stdio zombie (green flash, exe alive) — bridge cannot fix host transport

## Consequences

**+** One `cdp_deploy mode=apply` call returns final state — no agent procedure.  
**+** No ensurer vs supervisor race during apply.  
**+** Lifecycle poll works through service restart.  
**−** Bridge references `TerminalMcp.Core` (job store SSOT).  
**−** Long apply blocks MCP CallTool up to `CDP_BRIDGE_DEPLOY_WAIT_MS` (intentional).

## References

- `CdpBridgeInvokeRouter`, `CdpBridgeDurableAccess`, `CdpBridgeDeployPolicy`
- `CdpBridgeServiceEnsurer.ShouldSuppressAutoStart`
- Tests: `CdpBridgeDeployGapTests`
