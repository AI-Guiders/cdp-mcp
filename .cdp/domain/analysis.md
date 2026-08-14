# Domain card: Analysis / CodeClones

- id: `analysis`
- organ: `analysis_scene` / CodeClones
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Run+grouping façade stays slim; Extract partial owns Roslyn windows/fingerprint/corpus.
- Anchors only — never path dumps in clone groups.
- Explore full-a: ADR-mapped mutate needs ExploreCorrLatch (corr dig or no_adr why=) — CDP-ADR-0031.

## Entry

- `CodeClones.Run` · analysis_scene clone detect
- `Correspondence.Run` stamps latch · `feature=no_adr why=` · `ExploreCorrGate` on buffer edit/create

## Antipatterns

- Growing Run/GroupClones with ExtractWindows/corpus — peel to `CodeClones.Extract*.cs`.
- Letting Extract.cs absorb corpus walk again — keep `Extract.Corpus`.
- Declaring Act/Done after writing ADRs without corr dig (half-a Explore).

## last_ship

- 2026-08-14 ExploreCorr full-stack adr.map: organ prefixes + `*`=CDP-ADR-0020 baseline (gate arms everywhere); cascade-ide Features/* denser · longest-prefix resolve
- 2026-08-14 ExploreCorr 3-layer: latch+gate+no_adr · seeming Done tooth · adr.map Glass/cdp organs · CDP-ADR-0031
