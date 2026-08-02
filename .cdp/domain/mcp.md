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

- 2026-08-02 → **0.5.510**: citizen `@intent mcp` host-execute → `McpOutletHabitat.DispatchAsync` (scene/call/mount/tools/…). Dig: go=mcp place-only.
- Ops FileLines near-miss peel: helpers → `McpOutletHabitat.Ops.Helpers.cs` (Ops 296→159) @ 0.5.455 · 2026-08-02
- soft-warn: `McpOutletHabitat` → `McpOutletHabitat.Ops.cs` (CallAsync→helpers) @ 0.5.378; main~289 / Ops~312
