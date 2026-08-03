# Domain card: DocumentEditPlane (cdp_buffer dispatch)

- id: `documenteditplane`
- organ: `cdp_buffer` / `cdp_doc_*` dispatch surface
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `DocumentEditPlane` is `partial` by concern.
- Partials: Core (Dispatch/Open/Create/Read/Reload/DiskPeek) · Edit (EditAsync/Thrash/EditApplied) · Anchor (ApplyAnchor/path resolve) · Diagnostics (DiagnosticsAsync/Close/arg helpers).
- Store/mutate stay in `DocumentBufferStore` / `DocBuffer` — this plane is the MCP dispatch + almost-online result shape.

## Entry

- `DocumentEditPlane.DispatchAsync` / `IsDocTool`

## Antipatterns

- Re-inlining Edit + Anchor + Diagnostics into one mega-file past soft-warn.
- Bypassing PathMutateGate via Cursor host Write for desk SSOT.

## last_ship

- **0.5.649** — csharp `place=before|after` on `M:` inserts **inside** method/type braces (`TryGetBlockInteriorInsertPoint`), not outside the member (same footgun class as ignored `T:` → wrong edge). Sibling members → `M:TypeName`. Tests DocumentEditPlaneAnchorPlaceTests 11/11.
- soft-warn peel: Core253 Edit210 Anchor244 Diagnostics251 @ 0.5.400
