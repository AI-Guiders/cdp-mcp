# Domain card: Program (MCP host entry)

- id: `program`
- organ: top-level `Program.cs` / ListTools Meta / DispatchMeta
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; top-level statements stay in `Program.cs` (one TLS file).
- ListTools Meta catalog lives in `MetaToolCatalog` partials (Core/SoftOrgans/IdeLifecycle/HubShell); `BuildMetaTools()` → `MetaToolCatalog.Build()`.
- DispatchMeta switch lives in `MetaDispatch` partials (Core/Ide/Hub) + `MetaDispatchDeps` (per-call record); Program keeps thin `DispatchMetaAsync` stub + TLS wiring.
- Deps rebuilt each call so mutable `workspaceStore` / `serverRef` stay current.

## Entry

- `Program.cs` · `MetaToolCatalog.Build` · `MetaDispatch.DispatchAsync` · `DispatchMetaAsync` stub

## Antipatterns

- Re-inlining Meta(*) catalog or DispatchMeta switch into Program past soft-warn.
- Trying `partial` on top-level statements file — peel to static types instead.
- Capturing `workspaceStore` once into a long-lived Deps instance (stale after EnsureWorkspaceDb).

## last_ship

- soft-warn: `DispatchMetaAsync` → `MetaDispatch*.cs` + `MetaDispatchDeps` @ 0.5.391; Program~642 / Core~356 / Ide~329 / Hub~396 / MetaDispatch~109
- prior: `BuildMetaTools` → `MetaToolCatalog*.cs` @ 0.5.390
