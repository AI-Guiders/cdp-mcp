# CDP-ADR-0200: Sidecar tenant multiplex (one IDE · N composers)

**Status:** Accepted · Implemented  
**Date:** 2026-08-21  
**Extends:** ADR-0198 (sidecar) · ADR-0199 (workspace isolation) · ADR-0030 (multiplex direction)

## Problem

ADR-0198 moved substrate to durable `CdpService`. All bridges share **one** `CdpHostRuntime` and static `CdpProfile`. Parallel Cursor chats (Forge / ANPM / CDP) clobber `cdp_open`, buffers, shell, WitDB — worse than monolith per-process isolation.

## Decision

**One `mcp.json` entry · one service · N tenant slices.**

### Tenant key (bridge → service headers)

| Header | Source |
|--------|--------|
| `X-CDP-Bridge-Session` | UUID at bridge startup (one stdio connection) |
| `X-CDP-Workspace-Key` | hash(sorted MCP client roots) or session fallback |
| `X-CDP-Composer` | `cdp_context composer=` latch; default `main` |

Full key: `{bridge}:{workspace}:{composer}`

### Service

- `CdpSharedKernel` — backends, settings, tool catalogs (once)
- `CdpTenantRegistry` — lazy `CdpTenantSlice` per key
- Invoke/capabilities route through tenant; `CdpProfile` + organs bound via `AsyncLocal`

### Per-tenant (isolated)

Session, buffers, shell, WitDB, LSP/build context, pressure/ignite under tenant state_root.

### Shared (read-mostly)

memory_world/skill, backend modules, deploy, global ignite host.

## Wire

```
Chat A ─ bridge A ─┐
Chat B ─ bridge B ─┼─▶ CdpService
                   │     tenant A / B / C
Chat C ─ same window composer latch ─┘
```

## Consequences

+ Parallel projects in separate chats without extra MCP entries.  
+ ADR-0199 disk layout preserved per workspace hash inside tenant.  
− Memory ∝ active tenants; idle eviction after TTL.  
− Same-window multi-chat needs `composer=` latch on `cdp_context`.
