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

- soft-warn: `McpOutletHabitat` → `McpOutletHabitat.Ops.cs` (CallAsync→helpers) @ 0.5.378; main~289 / Ops~312
