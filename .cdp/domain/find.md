# Domain card: Find in Files

- id: `find`
- organ: `find` / EditorComfort
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch façade stays slim; Search partial owns rg/root/hits.
- IdeFindChannel.Shape peels: Shape (result/stubs/paths) · Shape.Last (last/clear/refine).
- scope=project|files|external; external needs path=.

## Entry

- `FindInFiles.Dispatch` · EditorComfort find scopes
- `IdeFindChannel` · Shape / Shape.Last

## Antipatterns

- Growing Dispatch with rg helpers — peel to `FindInFiles.Search.cs`.
- Re-inlining Last/Refine into Shape past soft-warn.

## last_ship

- soft-warn near-miss: IdeFindChannel.Shape.Last156 · Shape243 @ 0.5.405 (was Shape389)
- prior: `FindInFiles` → `FindInFiles.Search.cs` @ 0.5.379
