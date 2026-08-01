# Domain card: IdeFindChannel

- id: `search`
- organ: `find_desk` / IdeFindChannel / `cdp_search`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Handle/Run/TryBuildFindArgs + Opt helpers stay in main; Shape partial owns ShapeResult/Last/Clear/ResolvePaths/MatchExclude.
- Axes: what / where / shape (ADR-0009). Text engine = FindInFiles + buffer find.

## Entry

- `cdp_search` · `go=find_desk` · `IdeFindChannel.Handle`

## Antipatterns

- Growing Run with ShapeResult/ResolvePaths/Last cards — peel to `IdeFindChannel.Shape.cs`.

## last_ship

- soft-warn: `IdeFindChannel` → `IdeFindChannel.Shape.cs` (ShapeResult→MatchExclude) @ 0.5.388; main~340 / Shape~389
