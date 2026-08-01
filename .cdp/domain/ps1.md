# Domain card: Ps1Scene

- id: `ps1`
- organ: `ps1_scene` / Ps1Scene
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch/put/open/check façade stays slim; Run partial owns RunAsync/last/help/pwsh/path helpers.
- ISE habitat: put → check (AST) → run (pwsh -File) → last.

## Entry

- `cdp_ps1_scene` · `Ps1Scene.DispatchAsync`

## Antipatterns

- Growing Put/Check with RunPwsh/ResolvePwsh — peel to `Ps1Scene.Run.cs`.

## last_ship

- soft-warn: `Ps1Scene` → `Ps1Scene.Run.cs` (RunAsync→helpers) @ 0.5.383; main~252 / Run~382
