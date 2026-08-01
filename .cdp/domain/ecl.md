# Domain card: IdeChkChannel

- id: `ecl`
- organ: `ecl` / IdeChkChannel / alias `chk`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400 (ADX soft-warn 350); Handle+Build+Board/cards stay in main; Builtins catalog → `IdeChkChannel.Builtins.cs`; Eval owns Evaluate→DoLink; Mutate owns DoEnable→OverlayItem.
- Overlay: `ecl.overlay` (legacy `chk.overlay`); acks: `ecl.acks`.

## Entry

- `go=ecl` · alias `chk` · `IdeChkChannel.Handle`

## Antipatterns

- Re-merging Eval+Mutate+Builtins into main past soft-warn — keep peels.

## last_ship

- soft-warn: Builtins peel → `IdeChkChannel.Builtins.cs`; main 376→275 @ 0.5.421 · 2026-08-01
- soft-warn: `IdeChkChannel` → `IdeChkChannel.Eval.cs` + `IdeChkChannel.Mutate.cs` @ 0.5.389; Eval~253 / Mutate~278
