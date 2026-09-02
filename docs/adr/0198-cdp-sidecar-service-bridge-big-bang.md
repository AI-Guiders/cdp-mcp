# ADR 0198: CDP sidecar big-bang — durable CdpService + thin CdpMcpBridge

**Status:** Accepted  
**Date:** 2026-08-21  
**Extends:** cascade-ide [0165](../cascade-ide/docs/adr/0165-mcp-transport-stratification-stdio-http-and-host-matrix.md) (Tier B pattern)  
**Reference:** agent-forge FORGE-ADR-0014 B (stdio bridge → HTTP invoke)

## Problem

`CdpMcp.exe` is simultaneously:

1. Cursor's stdio MCP child (fragile JSON-RPC pipe)
2. Full agent-IDE substrate (session, WitDB, shell, LSP, build, ignite…)
3. Deploy target of `KillRunning` hard deploy

Any deploy/OOM/stdout pollution/remount kills the entire habitat. dbhub stays up because it is a thin gateway; forge stays up because Cursor talks to a thin bridge while Forge API is durable.

## Decision (big-bang)

Split into **three layers** (0165 §2.1):

| Layer | Artifact | Lifecycle |
|-------|----------|-----------|
| Transport (Cursor) | `CdpMcpBridge.exe` | Cursor child; cheap to remount |
| Durable SSOT | `CdpService.exe --service` | User daemon; survives deploy/remount |
| Handlers | Existing `ProgramHost` / backends | In-process inside CdpService only |

### Wire

```
Cursor ──stdio──▶ CdpMcpBridge ──HTTP──▶ CdpService (:8771)
                      │                      │
                      │                      ├─ POST /api/v1/cdp/invoke
                      │                      ├─ GET  /api/v1/cdp/capabilities
                      │                      ├─ GET  /healthz
                      │                      └─ POST /mcp  (Streamable HTTP MCP)
```

### Deploy

- `publish-and-deploy.ps1 -Mode hard` **KillRunning** targets **CdpService only** (not bridge).
- Bridge binary updated in place; Cursor remount optional (nudge only if bridge stale).
- `Start-CdpService.ps1` ensures daemon before bridge connects.

### Config (`cdp-mcp.toml`)

```toml
[service]
enabled = true
bind = "127.0.0.1"
port = 8771
token_path = ""  # default: %LocalAppData%/cdp-mcp/service-token
```

Bridge reads `base_url` from `[service]` or `CDP_SERVICE_URL`.

### Security

- Loopback bind only (`127.0.0.1`).
- Random session token in token file; bridge sends `Authorization: Bearer …`.
- No roslyn/debug over network to non-localhost (unchanged Tier A rules).

### Legacy

- `CdpMcp.exe` without `--service` and without bridge config: **deprecated monolith stdio** (remove after dogfood window).
- `CDP_RELOAD_NUDGE` remains for bridge remount; **not** required for service hot-swap.

## Consequences

**+** Deploy no longer drops agent session/shell/WitDB.  
**+** Same pattern as forge/dbhub-http.  
**−** Two processes to monitor; bridge must fail clearly when service down.  
**−** Tests cover HTTP + stdio bridge paths.
