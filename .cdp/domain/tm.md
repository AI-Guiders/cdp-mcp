# Domain card: Task Manager (plan / WitDB)

- id: `tm`
- organ: `plan` / `IdeTaskManager`
- product: `#CDP`

## Invariants

- Feature = Intent; Task = Stage; tree via `ParentId` + `Ordinal`.
- Focus/done mutate WitDB; board chrome `@phase` / `#Product` — affinity/tag, не status.
- Incomplete leaf (0.5.309+): `pending|active`, skip handoff; leaf = no incomplete children.
- `feature_focus` / `task_focus` / `done` → leaf resolve + `leaf-wake` AutoI (2s).
- REPL: bare `feature X` = upsert/dedupe (+ leaf-arm on dedupe); `feature focus X` → `feature_focus` (0.5.311+).
- `done` / `shipped` with **feature title** (or bare when feature focused, no task) → close incomplete leaves + clear focus (0.5.412).
- `shipped` without prior `start` → implicit wall start (ADX ceremony tax).
- Soft-warn FileLinesWarn=400; `IntentWorkspaceStore` is `partial` by concern (Core/Intent/Stage/Scene/Persist/Find + Leaf/StageCriteria(+Norm)/StageEvents/StageProduct).
- WitDB path = `StateRoot/{seat}/intent-workspace.witdb` (per-seat; dual seats never share FileShare.None). Primary `cdp` once Moves legacy flat file.

## Entry

- Cockpit `cmd=` / `go=plan`
- `IdeTaskManager.Dispatch` · `Mutations.Feature|Task.*` · `IntentWorkspaceStore.Leaf`

## Antipatterns

- Asking operator how focus/done works without reading Leaf + Mutations.
- Seeding task titles that contain REPL verbs (`|focus|start`).
- Re-inlining Intent/Stage/Scene/Persist/Find into one mega-file past soft-warn.
- Treating `done invent Feature…` as "task not found" when feature exists — fixed 0.5.412.

## last_ship

- 2026-08-02: cockpit `go=plan` after Ensure must `RequireWorkspace()` — deps.WorkspaceStore is null snapshot @ process start @ 0.5.449
- 2026-08-02: per-seat WitDB (`WorkspaceDbPaths` + bootstrap FileGate + skip EnsureCreated when seat file exists) @ 0.5.448 — dogfood `cdp_open` → `recent_store=…/cdp/intent-workspace.witdb`. Migrated 442MB DB was torn (FreePage≥TotalPageCount) from prior dual-seat FileShare fights; quarantined `*.torn-dualseat-*.bak`, fresh seat DB.
- StageCriteria.Norm peel (≤ADX soft-warn; CRUD206 + Norm170) @ 0.5.430 · 2026-08-01
- 0.5.416 `FindIntentIdByTitle` StripBoardChrome + unique prefix (parity with FindStageMatching); peel Find.Title
- 0.5.412 done/shipped feature-title fallback + shipped without start
- soft-warn peel: Core126 Intent140 Stage264 Scene221 Persist318 Find373 @ 0.5.401 (Leaf148 StageCriteria206+Norm170 StageEvents200 StageProduct60)
- 0.5.309 leaf AutoI — `6a97f48` (2026-07-30)
- 0.5.310 Domain axis + remount domain pulse (pressure checklist + charge appendix)
- 0.5.311 FeatureAdd dedupe leaf-arm + REPL `feature focus X`
- 0.5.312 Soft organ `go=domain` — reconstruction chains [A] (`name→edges→entry→≠`)
