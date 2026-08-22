# Domain card: MCP outlet

- id: `mcp`
- organ: `cdp_mcp`
- product: `#CDP`

## Invariants

- Habitat verbs: scene|presets|mount|tools|call|unmount via `DispatchAsync`.
- Soft-warn FileLinesWarn=400; peel Ops (Call→helpers) rather than grow main.

## Entry

- `cdp_mcp` · `McpOutletHabitat`

## Antipatterns

- Touching QualityGates.cs for soft-warn (EOL-dirty).

## last_ship

- **2026-08-22 bridge deploy-gap survival (ADR-0203)** — `CdpBridgeInvokeRouter`: apply|hard|rollout blocks until durable job + health; lifecycle local fallback from `DurableJobStore`; ensurer suppress during in-flight deploy; `bridge_wait=false` opt-out. Tests `CdpBridgeDeployGapTests`. Bridge 0.2.0.
- **2026-08-21 bridge auto-start ensurer** — `CdpBridgeServiceEnsurer`: `[service] install_dir` + `auto_start`; file lock (no await-under-Mutex — thread-affine ReleaseMutex fix); probe `/healthz`; spawn sidecar; retry HTTP once. Test `CdpBridgeServiceEnsurerTests`.
- **2026-08-21 bridge Bearer auth (401 fix)** — `CdpBridgeTenantHeadersHandler` now sends `Authorization: Bearer` on all outbound invoke/capabilities requests (regression from ADR-0200 latch-only auth); token reload on 401 from `service-token`. Test `CdpBridgeTenantHeadersHandlerTests`.
- **2026-08-21 ADR-0200 per-conversation tenant (multi-chat one Agents)** — `X-CDP-Conversation-Id` from MCP `_meta` (`cursor/composerId` | `progressToken`); per-conversation `CdpTenantComposerLatch`; bridge AsyncLocal; `cdp_context composer=` scoped to conversation. Tests 9/9.
- **2026-08-21 ADR-0200 tenant multiplex (full)** — `CdpSharedKernel` + `CdpTenantRegistry` (idle eviction); per-tenant Session/buffers/**shell**/WitDB/settings/pressure state_root; bridge roots→workspace hash + `X-CDP-*` headers; `cdp_context composer=` latch + `/tenant/composer`; `CdpTenantDispatch`. Tests 5/5.
- 2026-08-02 → **0.5.510**: citizen `@intent mcp` host-execute → `McpOutletHabitat.DispatchAsync` (scene/call/mount/tools/…). Dig: go=mcp place-only.
- Ops FileLines near-miss peel: helpers → `McpOutletHabitat.Ops.Helpers.cs` (Ops 296→159) @ 0.5.455 · 2026-08-02
- soft-warn: `McpOutletHabitat` → `McpOutletHabitat.Ops.cs` (CallAsync→helpers) @ 0.5.378; main~289 / Ops~312
