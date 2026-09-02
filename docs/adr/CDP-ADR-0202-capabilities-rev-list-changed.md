# CDP-ADR-0202: Online capabilities refresh (bridge `list_changed`)

**Status:** Accepted  
**Date:** 2026-08-21  
**Extends:** [0198](0198-cdp-sidecar-service-bridge-big-bang.md) (CdpService + CdpMcpBridge)  
**Related:** [0201](CDP-ADR-0201-cdp-peek-read-only-eyes.md) (new tools must appear without MCP remount)

## Problem

Cursor caches MCP tools after initial `tools/list`. The bridge advertises `ListChanged=true`, but in ADR-0198 layout:

1. **CdpService** runs HTTP-only — `CdpHostRuntime.NotifyListChanged()` had no `_serverRef` → notification was a no-op.
2. **CdpMcpBridge** only fetched `/capabilities` on Cursor-initiated `ListTools` — never pushed `notifications/tools/list_changed`.
3. After **deploy** or **session shortlist change** (`cdp_open`, `cdp_context`, desk layout), agents kept stale tool lists until manual MCP remount.

## Decision

### 1. `capabilitiesRev` on CdpService

- Monotonic revision per process: boot sequence + session bumps.
- Exposed on:
  - `GET /healthz` → `capabilitiesRev` (unauthenticated — bridge poll)
  - `GET /api/v1/cdp/capabilities` → `capabilitiesRev` + `tools[]`
  - `GET /api/v1/cdp/capabilities/watch` → SSE `event: rev` stream (authenticated)

### 2. Bump triggers (CdpService)

`CdpHostRuntime.NotifyListChanged()` now:

1. Bumps `capabilitiesRev` (always — HTTP path included).
2. Sends MCP `tools/list_changed` when wired to stdio monolith (`_serverRef`).

Existing call sites unchanged: `cdp_open`, `cdp_restore`, `cdp_context`, desk shortlist refresh.

Deploy: new CdpService process → new boot rev → bridge detects change.

### 3. Bridge watcher (CdpMcpBridge)

Background task while stdio MCP runs:

- **Primary:** SSE `/api/v1/cdp/capabilities/watch`
- **Fallback:** poll `GET /healthz` every 2s (`CDP_BRIDGE_CAPABILITIES_POLL_MS` override, 500–60000)

On rev change → `SendNotificationAsync(ToolListChangedNotification)` → Cursor re-lists tools.

## Consequences

**+** New tools (e.g. `cdp_peek`) appear after hard deploy without Cursor MCP toggle.  
**+** Session context changes refresh shortlist in Cursor automatically.  
**+** Works with durable sidecar — bridge remount optional.  
**−** Extra SSE connection + poll backup per bridge instance (loopback only).  
**−** Cursor must honor `ListChanged` (supported for MCP 2024-11-05).

## Verification

- `CdpCapabilitiesRevisionTests` — bump + watch stream.
- Manual: deploy service → bridge logs `list_changed (capabilitiesRev=…)` → `cdp_peek` visible in agent tool list.
