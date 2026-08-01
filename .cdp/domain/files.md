# Domain card: IdeFilesChannel

- id: `files`
- organ: `files_desk` / IdeFilesChannel / `cdp_files`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400 (ADX soft-warn 350); Handle/Scene/List/Cd/Stat/Tree/Roots façade stays in main; Open+Search → `IdeFilesChannel.Open.cs`; Text projection in `IdeFilesChannel.Text.cs`; Fs partial owns Board/Enumerate/WalkTree/ResolveCwd/Opt helpers.
- Utility organ (ADR-0016): `where=project|external|cwd` parity with search — not project-bound only.

## Entry

- `cdp_files` · `go=files_desk` · `IdeFilesChannel.Handle`

## Antipatterns

- Growing List/Tree with Enumerate/WalkTree/ResolveCwd — peel to `IdeFilesChannel.Fs.cs`.
- Re-inlining Open/Search into façade past ADX FileLinesWarn (350).

## last_ship

- soft-warn: Open+Search peel → `IdeFilesChannel.Open.cs`; main 375→280 @ 0.5.422 · 2026-08-01
- soft-warn: `IdeFilesChannel` → `IdeFilesChannel.Fs.cs` (Board→Dict) @ 0.5.386; Fs~361 (Text~215)
