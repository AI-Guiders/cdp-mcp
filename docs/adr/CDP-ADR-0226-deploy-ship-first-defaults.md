# CDP-ADR-0226: Deploy ship-first — defaults, rollout, observability

**Status:** Accepted  
**Date:** 2026-09-12  
**project-id:** `cdp-mcp` · extends ADR-0223 (ship slot), ADR-0203 (bridge deploy gap), ADR-0211 (deploy-plane)  
**Tags:** #cdp #adr #deploy #ship #rollout #just-culture

---

## Context

После ADR-0223 (`mode=ship` / immutable slot) операторский путь **всё ещё default'ился в hard**:

- `cdp_deploy` без `mode=` → `hard` sibling → KillRunning + lock на live `cdp-mcp.toml`.
- `mode=rollout` step 3 → `hard_sibling` — снова KillRunning вместо `apply_staged`.
- `cdp_ship deploy=true` → `deploy_mode=hard`.
- Citizen `@intent deploy` → `hard`.

Инцидент 2026-09-12 (deploy flake): harness `Client closed` + file lock на SSOT toml во время hard sibling — **активный** триггер на **латентной** модели «hard = routine ship».

## Decision

1. **Routine default = `mode=ship`** everywhere: `IdeDeploy.Run`, Citizen router, bridge policy, REPL bare `deploy`, `cdp_ship deploy=true`.
2. **`mode=hard`** — escape only (explicit token / `hard_deploy` alias); sibling default target preserved when hard is chosen.
3. **`mode=rollout`** = `soft_sibling` → `soft_self` → **`apply_staged`** (no `hard_sibling` / `hard_peer`).
4. **Seat config copy** (`CdpDeploySeatConfig.SeedFromLiveSeat`) — transient file-lock retry (parity with `CdpSlotRegistry`).
5. **LOSA pulse** — `IdeOpsPulse.DeployLosCard` / `ops_pulse` line: `deploy_in_flight`, `pending_staged`, `slot_count`.
6. **Bridge wait** — `ship` joins `apply|rollout|hard` as bridge-blocking modes (ADR-0203).

## Consequences

**+** Routine deploy не трогает bridge KillRunning и не держит lock на operator toml.  
**+** Dual-seat rotation без hard step 3 — меньше remount thrash.  
**+** Health/ops видит deploy state без shell archaeology.  
**−** Операторы, привыкшие к «голому deploy = hard», должны явно писать `mode=hard` для escape.

## Verification

- `CitizenDeployHostTests` — bare `@intent deploy` → `ship`.
- `IdeDeployTests` — REPL `deploy dry sibling` → `ship`; dry_run default → `ship`.
- `IdeDeployRepoRootTests` — rollout dry_run steps end at `apply_staged`, no `hard`.
- Live: `cdp_deploy` (default) or `cdp_deploy mode=ship` — slot activation without hard sibling.
