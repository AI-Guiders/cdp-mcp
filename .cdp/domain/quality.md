# Domain card: quality gates

- id: `quality`
- organ: `QualityGates` (+ `.Disk` / `.Eval` / `.Policy`)
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn from overlay `.cdp/quality-gates.toml` (this repo: warn=350).
- Default `go=quality` / Snap: **open buffers only** (cockpit alert must not thrash on closed fat files).
- `scope=disk|project|map`: whole-project `*.cs` map — warn/fail + near-miss (floor = suggest_sniper or warn−50); skip bin/obj/.git.
- Disk scan is **file lines only** (no method scan) — ADX token tax.
- Hub `QualityGates.cs` stays under soft-warn; buffer eval + policy/load live in peels.

## Entry

- `go=quality` — open buffers
- `go=quality scope=disk` — project map; `limit=` caps shown findings (default 40)
- `go=quality scope=assert` — ADX assertion catalog (`.cdp/assertions.toml`) + kernels
- Tune: `.cdp/quality-gates.toml`
- Catalog: `.cdp/assertions.toml`

## Antipatterns

- Shell Measure-Object / Get-Content.Count as first dig for near-miss.
- Turning disk map into always-on Snap (alert noise).
- Re-inlining peels past FileLinesWarn.

## last_ship

- InternetBrowserHabitat.Engine→Lynx peel (≤ADX soft-warn) @ 0.5.434 · 2026-08-01
- FindInFiles.Rg + Search.Util peel (≤ADX soft-warn) @ 0.5.433 · 2026-08-01
- DocumentBufferStore.Edit peel (≤ADX soft-warn) @ 0.5.432 · 2026-08-01
- IdeChangePlanner.Persist peel (≤ADX soft-warn) @ 0.5.431 · 2026-08-01
- StageCriteria.Norm peel (≤ADX soft-warn) @ 0.5.430 · 2026-08-01
- IdeDeskSeats.Presets peel (≤ADX soft-warn) @ 0.5.429 · 2026-08-01
- Fire.Charge peel (≤ADX soft-warn) @ 0.5.428 · 2026-08-01
- Groups.Infer peel (≤ADX soft-warn) @ 0.5.427 · 2026-08-01
- ADX-HX-001 live + NativeDialogs.Win32 @ 0.5.426 · 2026-08-01
- ADX assertions + Z3 kernel proofs (scope=assert) @ 0.5.425 · 2026-08-01
- QualityGates.Eval + .Policy peel under soft-warn @ 0.5.417 · 2026-08-01
- EvaluateDisk + soft-organ scope=disk @ 0.5.409 · 2026-08-01
