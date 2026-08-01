# Domain card: Ps1Scene

- id: `ps1`
- organ: `ps1_scene` / Ps1Scene
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Dispatch/put/open/check façade stays slim; Run/Pwsh/Helpers partials own exec + path helpers.
- ISE habitat: put → check (AST) → run (pwsh -File) → last.

## Entry

- `cdp_ps1_scene` · `Ps1Scene.DispatchAsync`

## Antipatterns

- Growing Put/Check with RunPwsh/ResolvePwsh — peel to `Ps1Scene.Pwsh` / `.Helpers`.
- Re-inlining peels past FileLinesWarn.

## last_ship

- soft-warn: `Ps1Scene.Run` → `.Pwsh` + `.Helpers` (Run~125) @ 0.5.419 · 2026-08-01
- soft-warn: `Ps1Scene` → `Ps1Scene.Run.cs` (RunAsync→helpers) @ 0.5.383; main~252 / Run~382
