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

- **2026-08-21 cdp_peek find+peek pathway (ADR-0201)** — auto regex for `|` alternation; bare filename `glob=` → `**/name`; lazy-bind project root before rg; zero-hit hints. Tests: Find_alternation_auto_regex, Find_bare_filename_glob_normalized, Find_lazy_bind_from_path @ 9ff5189. Pending service restart (no hard deploy).
- **2026-08-08 SoftFL ACCEPT ResolveRg habitat** — throw-Cursor: `ResolveRg` probes habitat bin / WinGet BurntSushi / beside-exe before Cursor-only PATH. Fail pulse carries `detail=` (timeout honesty). Tests ResolveRg_finds_habitat_or_path. Live dual build_utc=2026-08-08T07:05:26Z.
- extract method_lines FindInFiles.Dispatch → Dispatch.cs helpers @ **0.5.475** · 2026-08-02
- soft-warn near-miss: IdeFindChannel.Shape.Last156 · Shape243 @ 0.5.405 (was Shape389)
- prior: `FindInFiles` → `FindInFiles.Search.cs` @ 0.5.379
