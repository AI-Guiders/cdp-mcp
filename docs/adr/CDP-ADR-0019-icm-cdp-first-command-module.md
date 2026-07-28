# CDP-ADR-0019: ICM — CDP-first IdeCommandModule (CIDE as projection)

**Status:** accepted (v0 seam)  
**Date:** 2026-07-28  
**project-id:** `cascade-ide` · consumer: CDP / Agent Env  
**Tags:** #cdp #adr #icm #command-module #cide #harness #parity

**Related:** cascade-ide ADR 0197 (cockpit wire parity) · 0200 (CDPCOPE) · 0199 (dual-agent profiles) · CDP-ADR-0018 (pressure) · Part2 Cognitive Platform

---

## Context

CIDE still has multiple execution paths (GUI commands, MCP loopback, soft organs). CDP desk (`cdp_*` / `go=` / seats) is the more coherent model. Porting CIDE `IdeCommands` "up" as SSOT would freeze the wrong language.

Operator framing (Part2):

1. **Unified ICM** — all CIDE execution paths become **projections** onto one module.
2. **Anchor Start/Stop** — agent toggles operator CIDE GUI cockpit (deferred).

Week DoD is harness seams, not Avalonia rewrite.

---

## Decision

1. **CDP owns ICM.** `IdeCommandModule` is the host execute seam: `command_id` + args → JSON/text result. MCP `CallToolHandler` binds to existing `DispatchAsync` (planes → meta → bare IDE → domain).
2. **CIDE = adapter.** Future CIDE GUI / in-proc callers invoke the same `command_id` surface (tool Meta / go verbs). They do **not** grow a parallel IdeCommands catalog as SSOT.
3. **Profiles share language:** `agent-only` (no GUI) and `dual-cockpit` (thin CIDE shell + same ICM) — not two desk dialects.
4. **SoftDispatch stays behind ICM.** `go=` soft organs remain routed inside cockpit/dispatch; ICM does not re-implement SoftDispatch in v0 — it is the outer execute door.
5. **Branch-first.** Ship on `feat/icm-command-module`; do not hard-deploy over live `main` until dogfood green.

---

## Non-goals (v0)

- Avalonia rewrite / dump Avalonia (separate epic).
- Moving all of `DispatchAsync` out of `Program.cs` in one PR.
- Anchor Start/Stop operator GUI (deferred stage).
- Treating CIDE IdeCommands as the source of truth.

---

## Consequences

- One bind point for tests and future CIDE wire.
- Next slices: extract dispatcher pieces behind ICM; wire CIDE thin adapter; then Start/Stop.
- Rollback = switch branch + redeploy.

---

## Ship notes

- `IdeCommandModule.Bind(DispatchAsync)` before `server.RunAsync`.
- CallTool uses `IdeCommandModule.ExecuteAsync`.
- Tests: bind/unbind + execute passthrough.
