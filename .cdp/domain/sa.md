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
- Citizen host-execute: `@intent sa|sa_desk|cdp_sa|code_sa|pre_sa|sa_code` → `CitizenRouteHost.RunSa` → Meta `cdp_sa` (no steal `go=sa` EICAS)

## Antipatterns

- Seeding FileLines peel when main is already under warn — dig quality gates (method_lines) first.
- Multi-`@intent sa` dogfood without path — pulse/slim still RunGates over open buffers; looks like Cloud.ru hang, is local.

## last_ship

- **2026-08-07** — SoftFL densest: `CideSaDeskLatch.Publish` → `Task.Run` fire-and-forget (FileShare still hung dogfood when Glass held latch); sync under `RootOverrideForTests`. Prior same-day: FileShare/bounded copy · depth→pulse · EnsureSaDeskDepth · RunSa direct HandleJson · slim `-uno`.
- **2026-08-07 earlier** — SoftFL hang ship: CideSaDeskLatch Publish non-blocking (File.Move into Glass-locked latch hung MCP; orphan `.tmp` evidence) · IdeSaChannel missing depth→pulse · EnsureSaDeskDepth · citizen RunSa direct HandleJson · slim `-uno`.
- **0.5.625** — `depth=pulse` cheap: no EvaluateStore; dirty=porcelain `-uno` count (+ locus flag); slim/full unchanged. Lived: citizen single `sa depth=pulse`. Why: multi-sa dogfood looked like FM hang; AutoI stuck firing under Composer Stop.
- **0.5.624** — citizen `@intent sa|*` host-execute Meta `cdp_sa` + PlaceOrgan(sa_desk); depth slim/full/pulse; no steal go=sa · dogfood ack=5/5 · tests 9/9 · 2026-08-03
- wave18 DIG REJECT FileLines peel — main under warn; buffers gates ok; method_lines already cleared @ 0.5.451 · 2026-08-02
- method_lines: extract `FormatPulse` + `BuildDeskPayload` from `Handle` (75→~26) @ 0.5.451 · 2026-08-02
