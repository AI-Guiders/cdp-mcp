# Domain card: Analysis / CodeClones

- id: `analysis`
- organ: `analysis_scene` / CodeClones
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Run+grouping façade stays slim; Extract partial owns Roslyn windows/fingerprint/corpus.
- Anchors only — never path dumps in clone groups.

## Entry

- `CodeClones.Run` · analysis_scene clone detect

## Antipatterns

- Growing Run/GroupClones with ExtractWindows/corpus — peel to `CodeClones.Extract.cs`.

## last_ship

- soft-warn: `CodeClones` → `CodeClones.Extract.cs` (ExtractWindows→corpus helpers) @ 0.5.380; main~245 / Extract~388
