# Domain card: ScriptScene

- id: `script`
- organ: `script_scene` / ScriptScene
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch/put/open/check/run façade stays slim; Helpers partial owns path/remember/board/FlattenJson.
- CSX habitat: put → check → run → last.

## Entry

- `cdp_script_scene` · `ScriptScene.DispatchAsync`

## Antipatterns

- Growing Run/Check with FlattenJson/TryResolve — peel to `ScriptScene.Helpers*.cs`.
- Letting Helpers.cs reabsorb FlattenJson/last-run — keep `Helpers.Flatten`.

## last_ship

- soft-warn near-miss: peel FlattenJson→BoolOr (+TryGetLast/path wire) → `ScriptScene.Helpers.Flatten.cs` @ 0.5.407; Helpers~195 / Flatten~205
