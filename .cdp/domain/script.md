# Domain card: ScriptScene

- id: `script`
- organ: `script_scene` / ScriptScene
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; façade = Dispatch/put/open/last/help; Check→`ScriptScene.Check.cs`, Run→`ScriptScene.Run.cs`; Helpers partial owns path/remember/board/FlattenJson.
- CSX habitat: put → check → run → last.

## Entry

- `cdp_script_scene` · `ScriptScene.DispatchAsync`

## Antipatterns

- Growing Run/Check with FlattenJson/TryResolve — peel to `ScriptScene.Helpers*.cs` / `.Check` / `.Run`.
- Letting Helpers.cs reabsorb FlattenJson/last-run — keep `Helpers.Flatten`.
- Re-inlining Check/Run into façade past ADX FileLinesWarn (350).

## last_ship

- soft-warn: Check+Run peels (`ScriptScene.Check.cs` / `.Run.cs`); façade 376→244 @ 0.5.420 · 2026-08-01
- soft-warn near-miss: peel FlattenJson→BoolOr (+TryGetLast/path wire) → `ScriptScene.Helpers.Flatten.cs` @ 0.5.407; Helpers~195 / Flatten~205
