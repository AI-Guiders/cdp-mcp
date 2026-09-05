# CDP-ADR-0211: Deploy-plane ownership — promote never runs from the target

**Status:** accepted
**Date:** 2026-09-05
**project-id:** `cdp-mcp` · supersedes nothing · extends ADR-0203 (deploy gap), ADR-0209 (gatekeeper slots), ADR-0032 (durable jobs)
**Tags:** #cdp #adr #deploy #self-restart #lock

---

## Context

Incident 2026-09-05: `cdp_deploy apply` (self) wedged for 8+ minutes.

- The durable job worker is spawned from `Environment.ProcessPath` of the enqueuer —
  for a self-deploy that is `D:\cdp-service\CdpService.exe`, i.e. **the worker's own exe
  lives in the directory the promote replaces**.
- `StopLockHoldersUnder` correctly skips the calling process (ADR-0203), but the worker
  still holds its own exe/dll — robocopy cannot replace them.
- The bridge auto-start suppression (`ShouldSuppressAutoStart`) checks an **in-flight job
  lease**; a stale/wedged job's lease expired mid-promote, so the bridge re-raised the
  service on **old** binaries, re-locking the payload. The tower itself stayed clean
  (ADR-0209 doctrine: never deploys, never restarts).

## Decision

1. **Deploy workers run from a disposable clone.** `IdeLifecycleJobs.StartDurable` clones
   the worker tree into `%LocalAppData%/cdp-mcp/workers/<guid>/` for `kind=deploy` jobs and
   points `worker_exe_path` at the clone. The runner cleans it up best-effort after the job.
2. **Self-lock guard (fail-fast).** `CdpDeployOrchestrator.Apply` refuses to run when
   `AppContext.BaseDirectory` is under `ServiceInstall` — no silent 8-minute wedges.
3. **Time-fenced deploy lock.** `CdpDeployLock` writes `<install>/deploy.lock` for the
   duration of a promote (TTL 5 min). Every starter respects it:
   - bridge ensurer: `ShouldSuppressAutoStart` = in-flight deploy job **or** fresh lock
     (the lock does not depend on job-store lease semantics);
   - `CdpServiceControl` starters are invoked by the promote itself (holds the lock).
4. **Tower doctrine unchanged.** `CdpGatekeeper` stays a stateless proxy (ADR-0209);
   deploy execution stays with the CDP plane — the worker, now from a clone.

## Consequences

- A promote can no longer deadlock on its own binary, and cannot be raced by a bridge
  re-raising a stale service.
- Lock TTL (5 min) must exceed the longest promote; a crashed worker self-heals by TTL.
- Worker clone adds one tree copy (~seconds) per deploy job — acceptable.

## Verification

- `DeployPlaneOwnershipTests` (4 facts): lock acquire/release/TTL, clone copy-tree,
  fallback when exe copy fails.
- Live check: `cdp_deploy soft` + `apply` on self — worker runs from clone, promote
  replaces install bits, service restarts healthy with the new build stamp.

---
