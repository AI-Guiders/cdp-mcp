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

- Growing Run/GroupClones with ExtractWindows/corpus — peel to `CodeClones.Extract*.cs`.
- Letting Extract.cs absorb corpus walk again — keep `Extract.Corpus`.

## last_ship

- soft-warn near-miss: peel `TryCollectCorpus`→`ResolveUserPath` (+`MemberName`) → `CodeClones.Extract.Corpus.cs` @ 0.5.406; Extract~217 / Corpus~182
