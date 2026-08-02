# Domain card: IdeSaChannel (sa_desk)

- id: `sa`
- organ: `sa_desk` / IdeSaChannel / Meta `cdp_sa`
- product: `#CDP`

## Invariants

- Soft-warn: project FileLinesWarn=350 / MethodLinesWarn=70. Main `IdeSaChannel.cs`=269 (&lt;350); Decide/Helpers already peeled.
- Axes: locus / scope / depth. Not EICAS `go=sa`.
- Payload builders: `FormatPulse` + `BuildDeskPayload` keep `Handle` under method warn (~26 lines).

## Entry

- `go=sa_desk` · aliases `code_sa` / `cdp_sa` · `IdeSaChannel.Handle`

## Antipatterns

- Seeding FileLines peel when main is already under warn — dig quality gates (method_lines) first.

## last_ship

- wave18 DIG REJECT FileLines peel — main under warn; buffers gates ok; method_lines already cleared @ 0.5.451 · 2026-08-02
- method_lines: extract `FormatPulse` + `BuildDeskPayload` from `Handle` (75→~26) @ 0.5.451 · 2026-08-02
