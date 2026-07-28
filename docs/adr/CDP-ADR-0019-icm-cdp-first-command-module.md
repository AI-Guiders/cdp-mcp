# CDP-ADR-0019: ICM — CDP-first IdeCommandModule (dual HCI, one drive)

**Status:** accepted (v0 seam + steer lock)  
**Date:** 2026-07-28  
**project-id:** `cascade-ide` · consumer: CDP / Agent Env  
**Tags:** #cdp #adr #icm #command-module #cide #harness #parity #intent-melody

**Related:** cascade-ide ADR 0197 (wire parity) · 0200 (CDPCOPE) · 0186 (navigation Anchor) · 0109/0081/0072 (Intent Melody) · CDP-ADR-0018 (pressure) · Part2 Cognitive Platform

---

## Context

Two products today: `cdp-mcp` (agent habitat) and `cascade-ide` (operator Avalonia). CDP desk is the coherent execute model. Porting CIDE `IdeCommands` up as SSOT freezes the wrong language.

Operator framing (Part2):

1. **Unified ICM** — one `command_id` drive.
2. **GUI on-demand** — former CIDE window is optional shell, not boot-with-GUI.
3. **Anchor Start/Stop** — agent opens/closes operator cockpit (deferred).
4. **Nav Anchor in GUI** — same `cdp_land` / Family:navigation wire; human entry later (parity, not second nav SSOT).

Week DoD = harness seams + locked model. Avalonia rewrite / repo merge = later.

---

## Decision

### 1. SSOT = CDP (`IdeCommandModule`)

`command_id` + args → result. MCP `CallTool` binds `DispatchAsync`. SoftDispatch / `go=` stay behind ICM.

### 2. Dual HCI, one drive (no permanent IdeCommands→ICM adapter)

| Actor | Own controls | Bottom |
|-------|----------------|--------|
| Agent | desk / `go=` / soft organs / MCP | `command_id` → CDP |
| Human | Intent Melody / chords / palette / GUI | same `command_id` → CDP |

Different hands, same transmission. GUI becomes an **on-demand CDP client/shell**, not a parallel command catalog. Do **not** keep a forever shim that translates IdeCommands into ICM — migrate chrome to call CDP natively (MCP or in-proc host).

### 3. Protect CIDE human settings / Melody

When GUI is optional or relocated: **do not break** Intent Melody catalog (`IntentMelody/`, `intent-catalog.toml`), chords/palette/`command_id` discoverability, or `CascadeIdeSettings` + user settings paths. Execute SSOT moves to CDP; Melody/settings remain operator discoverability + prefs until carefully migrated.

### 4. Profiles

- `agent-only` — no operator GUI (current MCP dogfood).
- `dual-cockpit` — thin GUI shell + same ICM (on-demand Start).

Not two desk dialects.

### 5. Branch-first

Ship on `feat/icm-command-module`; hard-deploy live `main` only after dogfood green.

---

## Non-goals (near term)

- Avalonia rewrite / dump Avalonia / physical monorepo merge (north star only).
- Permanent IdeCommands→ICM adapter layer.
- Breaking Melody / `CascadeIdeSettings` "заодно".
- Anchor Start/Stop implementation (next stage after ICM contract solid).

---

## Consequences

- Agent path already on ICM; human path converges by calling the same ids (`cdp_land`, `cdp_buffer`, …).
- Nav Anchor GUI = projection of existing land wire.
- Rollback = switch branch + redeploy.

---

## Ship notes

- v0: `IdeCommandModule.Bind(DispatchAsync)`; CallTool → `ExecuteAsync`; tests bind/passthrough.
- Steer lock (this amend): dual HCI, no forever adapter, Melody/settings invariant, GUI Anchor parity noted.
