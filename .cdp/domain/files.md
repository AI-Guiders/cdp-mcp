# Domain card: IdeFilesChannel

- id: `files`
- organ: `files_desk` / IdeFilesChannel / `cdp_files`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400 (ADX soft-warn 350); Handle/Scene/List/Cd/Stat/Tree/Roots façade stays in main; Open+Search → `IdeFilesChannel.Open.cs`; Text projection in `IdeFilesChannel.Text.cs`; Fs owns Board/Enumerate/WalkTree + Opt helpers; path/cwd → `IdeFilesChannel.Fs.Path.cs`.
- Utility organ (ADR-0016): `where=project|external|cwd` parity with search — not project-bound only.

## Entry

- `cdp_files` · `go=files_desk` · `IdeFilesChannel.Handle`

## Antipatterns

- Growing List/Tree with Enumerate/WalkTree — peel to `IdeFilesChannel.Fs.cs` / `.Fs.Path`.
- Re-inlining Open/Search/Path into façade past ADX FileLinesWarn (350).

## last_ship

- soft-warn: Path peel → `IdeFilesChannel.Fs.Path.cs`; Fs 361→266 @ 0.5.424 · 2026-08-01
- soft-warn: Open+Search peel → `IdeFilesChannel.Open.cs`; main 375→280 @ 0.5.422 · 2026-08-01
