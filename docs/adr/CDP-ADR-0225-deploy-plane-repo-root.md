# CDP-ADR-0225: Deploy plane source resolution (`repo_root`)

**Status:** Accepted  
**Date:** 2026-09-12  
**Extends:** [CDP-ADR-0211](CDP-ADR-0211-deploy-plane-ownership.md)

## Problem

`cdp_deploy mode=rollout` failed with `source_not_found` when:

- Coordinator workspace is not `cdp-mcp` (no `cdp_open`)
- `publish-and-deploy.ps1` picked `D:\cdp-service\CdpService.exe` as worker (no checkout)
- Apply from ServiceInstall hit ADR-0211 self-lock

Operators fell back to manual `dotnet publish` → `.next` → `mode=apply`.

## Decision

Resolve publish source in order:

1. Deploy args: `repo_search_root` / `repo_root` / `source_root`
2. Session: `project_root` / solution path (`cdp_open`)
3. Seat operator TOML: `[deploy] repo_root = "…/cdp-mcp"`

Also:

- `publish-and-deploy.ps1` passes `repo_search_root=$PSScriptRoot` and prefers repo-built worker for soft/hard/rollout
- `PinDeployLifecycle` pins durable deploy jobs from `[deploy].repo_root` when session is empty
- `IdeDeployCli` reads `repo_search_root` from JSON payload

## Non-goals

- Auto-discovery of clone path without operator config
- Removing ADR-0211 self-lock (apply still uses disposable worker clone)

## Verification

- `IdeDeployRepoRootTests`
- `cdp_deploy mode=rollout dry_run=true` from non-cdp workspace with seat `[deploy].repo_root`
