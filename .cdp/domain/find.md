# Domain card: Find in Files

- id: `find`
- organ: `find` / EditorComfort
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch façade stays slim; Search partial owns rg/root/hits.
- scope=project|files|external; external needs path=.

## Entry

- `FindInFiles.Dispatch` · EditorComfort find scopes

## Antipatterns

- Growing Dispatch with rg helpers — peel to `FindInFiles.Search.cs`.

## last_ship

- soft-warn: `FindInFiles` → `FindInFiles.Search.cs` (TryResolve→Hit) @ 0.5.379; main~267 / Search~358
