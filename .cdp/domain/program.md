# Domain card: Program (MCP host entry)

- id: `program`
- organ: top-level `Program.cs` / ListTools Meta / DispatchMeta
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; top-level statements stay in `Program.cs` (one TLS file).
- WitDB bootstrap: `WorkspaceDbHost` owns Ensure/Invalidate/Require (not re-inlined in TLS).
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

- WorkspaceDbHost peel (EnsureWorkspaceDb cluster) — Program.cs 396→341 @ 0.5.418 · 2026-08-01
- soft-warn peel: MetaToolCatalog Ide164 / Ide.Pkg162 (pkg→sln) @ 0.5.415
- soft-warn peel: MetaToolCatalog Soft210 / Soft.Ops144 (files→cockpit_host) @ 0.5.414
- soft-warn peel: MetaToolCatalog Core183 / Core.Ops212 (recent→sa) @ 0.5.413
- soft-warn near-miss: MetaDispatch.Hub → Hub163 / HubCsx202 / HubShell125 @ 0.5.403 (chain Core→Ide→Hub→HubCsx→HubShell).
- dig Program~396: TLS residual confirmed — next Program peel needs deps-bag static type (EnsureWorkspaceDb cluster), not partial.
- soft-warn residual: VisibleToolCatalog + IdeToolDispatch + CdpWorkDispatch @ 0.5.392; Program~396
- prior: DispatchMeta → MetaDispatch @ 0.5.391; MetaToolCatalog @ 0.5.390
