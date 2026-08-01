# Domain card: Program (MCP host entry)

- id: `program`
- organ: top-level `Program.cs` / ListTools Meta / DispatchMeta
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; top-level statements stay in `Program.cs` (one TLS file).
- ListTools Meta catalog lives in `MetaToolCatalog` partials (Core/SoftOrgans/IdeLifecycle/HubShell); `BuildMetaTools()` → `MetaToolCatalog.Build()`.
- DispatchMetaAsync + startup wiring remain in Program until next peels.

## Entry

- `Program.cs` · `MetaToolCatalog.Build` · `DispatchMetaAsync`

## Antipatterns

- Re-inlining Meta(*) catalog into Program past soft-warn.
- Trying `partial` on top-level statements file — peel to static types instead.

## last_ship

- soft-warn: `BuildMetaTools` → `MetaToolCatalog*.cs` @ 0.5.390; Program~1610 / Core~381 / Soft~340 / Ide~313 / Hub~253
