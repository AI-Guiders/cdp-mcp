# Domain card: quality gates

- id: `quality`
- organ: `QualityGates` (+ `.Disk` / `.Eval` / `.Policy`)
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn from overlay `.cdp/quality-gates.toml` (this repo: warn=350).
- **On `file_lines` / `method_lines` warn:** default response = **OOA&D + DRY + KISS** (named types by responsibility, facade thin, shared state owned once) — not SoftFL densify. Dig: `software.authoring` / `playbook-ooad-agent-operational-v1.md`.
- **`partial` is a narrow case** (codegen / designer / rare hand split of one type across files). Metric peel via `Foo.Bar.cs` ≠ design. `partial ≠ split` · tooth `partial_family`.
- Default `go=quality` / Snap: **open buffers only** (cockpit alert must not thrash on closed fat files).
- **`file_lines` / `suggest_sniper` — code buffers only** (`.cs`, `.fs`, `.ts`, …); prose/config (`.md`, `.toml`, `.json`, …) skipped on open-buffer eval. Disk map stays `*.cs`.
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
- **Partial peel mill** — splitting one type into many `Foo.*.cs` to silence per-file `file_lines` (partial ≠ seam). Tooth: `partial_family` (files≥warn or sum lines≥file_lines_warn). Brain still required; teeth ≠ infinite fence.
- **Seeming Senior** — claiming "extract seam" while adding another `Foo.Bar.cs` partial without a named type / ownership boundary. Dig first: call graph, shared Gate/Arms, who owns the loop. Prefer real type or honest dense exclude — never more dotted peels for metrics.

## Dig-before-peel (Senior ritual)

1. Treat FileLines as **design pressure**, not a peel ticket — run OOA&D/nouns-verbs (or short DRY/KISS cut) first.
2. Name the seam in one sentence (what changes together / who owns state).
3. If state is shared (`Gate`/`Arms`) — leave a thin schedule/API peel; move the **loop/probe** to a real type.
4. Wire callers + tests to the real type (no forever facade on the God type).
5. `partial` only when the compiler/tooling already requires one type across files — never as SoftFL.
6. Stamp domain `last_ship` with peel count delta — not "file_lines green".

## last_ship

- **2026-08-15 FileLines → OOA&D/DRY/KISS @ 0.5.719** — warn `go=` steers real types; `partial` stamped as narrow case · domain + gate copy · SoftFL invent REJECT
- **2026-08-07 wave1 real seams** — `IdeIgniteConnectionWatch` + `IdeIgniteOomWatch` extracted from ArmHost peels · schedule stays `IdeIgniteArmHost.OomSchedule` (Arms/Gate) · family 26→25 peels · tests ConnectionWatch 3/3 + PartialFamily 2/2 · dig-before-peel ritual stamped
- **2026-08-07 partial_family tooth** — `QualityGates.PartialFamily` · warn when ≥4 dotted peels **or** family sum ≥ file_lines_warn · message peel≠seam · `.g.cs`/Designer skipped · tests QualityGatesPartialFamily · SoftFL invent REJECT (operator-directed tooth)
- Soft FileLines BATCH close @ **0.5.640** — densest peels + Wire Head/Tail; top FileLines 341; feature CLOSED · VL#146 · 2026-08-03
- wave24 refactor_plan false-design fix @ **0.5.477** — non_csharp leave + top_level under-warn leave; tests green · 2026-08-02
- wave23 DIG REJECT — csproj FileLines open-buffer false hotspot (XML; disk `*.cs` ok); no method_lines left on Program/Health. Next: refactor_plan false-design fix or citizen · 2026-08-02
- wave22 HealthJson method extract @ **0.5.476** — HealthJson cleared; remaining soft open-buffer: CdpMcp.csproj FileLines=411 (design, not method peel) · 2026-08-02
- wave21 FindInFiles.Dispatch method extract @ **0.5.475** — Dispatch cleared; remaining soft: HealthJson=75 · 2026-08-02
- wave20 MetaDispatch.CoreAsync method extract @ **0.5.474** — CoreAsync off hotspot list; remaining method_lines: FindInFiles.Dispatch=197 · HealthJson=75 · 2026-08-02
- wave19 DIG REJECT BuildBudget — already peeled @0.5.452; sole remaining method_lines hotspot MetaDispatch.CoreAsync=267 (extract_method) · 2026-08-02
- IdeRefactorPlanChannel.Budget.BuildBudget method_lines peel (what-if helpers + FormatBudgetPayload) @ 0.5.452 · 2026-08-02
- FileLines batch peel×12 hubs→≤256L (ArchBoard, EditorPlane, Program, Find, PlanPulse, Settings, Qrh, Runners, EditSniper, MetaDispatch, Ignite, Scope) @ 0.5.441 · 2026-08-02
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
- Fire.Charge peel (≤ADX soft-warn) @ 0.5.428 · 2026-08-01
- Groups.Infer peel (≤ADX soft-warn) @ 0.5.427 · 2026-08-01
- ADX-HX-001 live + NativeDialogs.Win32 @ 0.5.426 · 2026-08-01
- ADX assertions + Z3 kernel proofs (scope=assert) @ 0.5.425 · 2026-08-01
- QualityGates.Eval + .Policy peel under soft-warn @ 0.5.417 · 2026-08-01
- EvaluateDisk + soft-instrument scope=disk @ 0.5.409 · 2026-08-01
