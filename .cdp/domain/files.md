# Domain card: IdeFilesChannel

- id: `files`
- organ: `files_desk` / IdeFilesChannel / `cdp_files`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Handle/Scene/List/Cd/Stat/Tree/Open/Search façade stays in main; Text projection in `IdeFilesChannel.Text.cs`; Fs partial owns Board/Enumerate/WalkTree/ResolveCwd/Opt helpers.
- Utility organ (ADR-0016): `where=project|external|cwd` parity with search — not project-bound only.

## Entry

- `cdp_files` · `go=files_desk` · `IdeFilesChannel.Handle`

## Antipatterns

- Growing List/Tree with Enumerate/WalkTree/ResolveCwd — peel to `IdeFilesChannel.Fs.cs`.

## last_ship

- soft-warn: `IdeFilesChannel` → `IdeFilesChannel.Fs.cs` (Board→Dict) @ 0.5.386; main~375 / Fs~361 (Text~215 unchanged)
