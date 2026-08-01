# Domain card: IdeChkChannel

- id: `ecl`
- organ: `ecl` / IdeChkChannel / alias `chk`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Builtins+Handle+Board/cards stay in main; Eval partial owns Evaluate→DoLink (probe/catalog merge/add); Mutate owns DoEnable→OverlayItem (enable/ack/persist).
- Overlay: `ecl.overlay` (legacy `chk.overlay`); acks: `ecl.acks`.

## Entry

- `go=ecl` · alias `chk` · `IdeChkChannel.Handle`

## Antipatterns

- Re-merging Eval+Mutate into main past soft-warn — keep peels.

## last_ship

- soft-warn: `IdeChkChannel` → `IdeChkChannel.Eval.cs` + `IdeChkChannel.Mutate.cs` @ 0.5.389; main~376 / Eval~253 / Mutate~278
