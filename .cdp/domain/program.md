# Domain card: Program (MCP host entry)

- id: `program`
- organ: top-level `Program.cs` / ListTools Meta / DispatchMeta
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; top-level statements stay in `Program.cs` (one TLS file).
- ListTools Meta catalog: `MetaToolCatalog` partials; `BuildMetaTools()` → `MetaToolCatalog.Build()`.
- ListTools composition: `VisibleToolCatalog` (+ SoftOrganMetaNames); `BuildVisibleTools()` thin stub.
- CallTool router: `IdeToolDispatch` (+ Deps per call).
- Meta switch: `MetaDispatch` partials + `MetaDispatchDeps` (per-call).
- cdp_work ops: `CdpWorkDispatch` (+ Deps per call).
- Deps rebuilt each call so mutable `workspaceStore` / `serverRef` stay current.

## Entry

- `Program.cs` stubs · `VisibleToolCatalog` · `IdeToolDispatch` · `MetaDispatch.DispatchAsync` · `CdpWorkDispatch`

## Antipatterns

- Re-inlining catalog/dispatch/work switch into Program past soft-warn.
- Trying `partial` on top-level statements file — peel to static types instead.
- Capturing `workspaceStore` once into a long-lived Deps instance.

## last_ship

- soft-warn residual: VisibleToolCatalog + IdeToolDispatch + CdpWorkDispatch @ 0.5.392; Program~396
- prior: DispatchMeta → MetaDispatch @ 0.5.391; MetaToolCatalog @ 0.5.390
