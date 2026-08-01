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

- Growing Run/Check with FlattenJson/TryResolve — peel to `ScriptScene.Helpers.cs`.

## last_ship

- soft-warn: `ScriptScene` → `ScriptScene.Helpers.cs` (TryBufferDiagnostics→BoolOr) @ 0.5.384; main~376 / Helpers~385
