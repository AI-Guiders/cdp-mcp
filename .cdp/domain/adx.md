# Domain card: ADX (agent DX in CDP product)

- id: `adx`
- organ: quality gates + desk ingress (product); letters = provenance only
- product: `#CDP`

## Invariants

- **ADX** = Agent Developer eXperience inside CDP desk — not open-letter text as the epic.
- North star: harness perception → agent-ready ingress → stamped memory → tokens ≈0.
- Soft-warn FileLines: prefer `go=quality scope=disk` over shell `Measure-Object`.
- ADX assertions: `go=quality scope=assert` loads `.cdp/assertions.toml`; kernels in-proc; Z3 proofs in tests (no Z3 natives in MCP publish).
- Default `go=quality` stays **open buffers**; disk map is opt-in (`scope=disk|project|map`).
- Dual-seat: `cdp_health` / `ops_pulse` show `self=` / `sib=` / `lag` — do not shell FileVersionInfo.
- Pressure wake: `op=recall` → ready when SSOT (body+plan/ignite); do not force 4-op ceremony.
- TM: `done`/`shipped` accept **feature title** (closes incomplete leaves); `shipped` without `start` starts wall implicitly.
- TM: `FindIntentIdByTitle` strips `@phase`/`#Product` + unique prefix (≥8) — same chrome rules as stage title match.

## Entry

- `go=quality scope=disk` — project `*.cs` FileLines warn/fail + near-miss band
- `go=quality scope=assert` — ADX assertion catalog (`.cdp/assertions.toml`) + kernels
- `cdp_health` → `seats` + `ops.self_version` / `sibling_version` / `lag`
- `.cdp/quality-gates.toml` — thresholds
- Letters (cold guidelines): `docs/open-letters/letter-of-agent-developer-experience.md`

## Antipatterns

- Shell line-count archaeology when disk quality map exists.
- Treating glossary/letter rewrite as product ADX ship.
- Re-inlining peels past FileLinesWarn.
- Assuming self install matches last hard sibling deploy without reading lag.

## last_ship

- IdePostmortemChannel.Draft peel (≤ADX soft-warn) @ 0.5.440 · 2026-08-01
- EditorComfort.FindNav.Nav peel (≤ADX soft-warn) @ 0.5.439 · 2026-08-01
- TakeShip.Helpers peel (≤ADX soft-warn) @ 0.5.438 · 2026-08-01
- LspOptionsToolkit.Recipes.Catalog peel (≤ADX soft-warn) @ 0.5.437 · 2026-08-01
- GoToAll.Match peel (≤ADX soft-warn) @ 0.5.436 · 2026-08-01
- MetaDispatch.Core.Health peel (≤ADX soft-warn) @ 0.5.435 · 2026-08-01
- InternetBrowserHabitat.Engine→Lynx peel (≤ADX soft-warn) @ 0.5.434 · 2026-08-01
- FindInFiles.Rg + Search.Util peel (≤ADX soft-warn) @ 0.5.433 · 2026-08-01
- DocumentBufferStore.Edit peel (≤ADX soft-warn) @ 0.5.432 · 2026-08-01
- IdeChangePlanner.Persist peel (≤ADX soft-warn) @ 0.5.431 · 2026-08-01
- StageCriteria.Norm peel (≤ADX soft-warn) @ 0.5.430 · 2026-08-01
- IdeDeskSeats.Presets peel (≤ADX soft-warn) @ 0.5.429 · 2026-08-01
- IdeIgniteArmHost.Fire.Charge peel (≤ADX soft-warn) @ 0.5.428 · 2026-08-01
- CdpPluginQuarantine.Groups.Infer peel (≤ADX soft-warn) @ 0.5.427 · 2026-08-01
- ADX-HX-001 habitat mutate + NativeDialogs.Win32 peel @ 0.5.426 · 2026-08-01
- ADX assertions catalog + recall/ignite Z3 kernels (scope=assert) @ 0.5.425 · 2026-08-01
- IdeFilesChannel Fs.Path peel (≤ADX soft-warn) @ 0.5.424 · 2026-08-01
- IdePluginsChannel Marketplace.Want peel (≤ADX soft-warn) @ 0.5.423 · 2026-08-01
- IdeFilesChannel Open+Search peel (façade ≤ADX soft-warn) @ 0.5.422 · 2026-08-01
- IdeChkChannel Builtins peel (façade ≤ADX soft-warn) @ 0.5.421 · 2026-08-01
- ScriptScene Check+Run peels (façade ≤ADX soft-warn) @ 0.5.420 · 2026-08-01
- Ps1Scene.Run → .Pwsh + .Helpers peel @ 0.5.419 · 2026-08-01
- WorkspaceDbHost peel — Program TLS EnsureWorkspaceDb @ 0.5.418 · 2026-08-01
- QualityGates.Eval + .Policy peel (hub ≤soft-warn) @ 0.5.417 · 2026-08-01
- FindIntentIdByTitle StripBoardChrome + prefix; Find.Title peel @ 0.5.416 · 2026-08-01
- MetaToolCatalog Ide→Ide.Pkg peel (pkg→sln) @ 0.5.415 · 2026-08-01
- MetaToolCatalog Soft→Soft.Ops peel (files→cockpit_host) @ 0.5.414 · 2026-08-01
- MetaToolCatalog soft-warn peel Core→Core.Ops (recent→sa) @ 0.5.413 · 2026-08-01
- TM done/shipped feature-title fallback + shipped without start @ 0.5.412 · 2026-08-01
- pressure recall SSOT auto-ready (ceremony tax cut) @ 0.5.411 · 2026-08-01
- ops/health dual-seat version pulse (`IdeOpsPulse` self/sib/lag) @ 0.5.410 · 2026-08-01
- prior: `go=quality scope=disk` @ 0.5.409 · 2026-08-01
