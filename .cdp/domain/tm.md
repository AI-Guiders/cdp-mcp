# Domain card: Task Manager (plan / WitDB)

- id: `tm`
- organ: `plan` / `IdeTaskManager`
- product: `#CDP`

## Invariants

- Feature = Intent; Task = Stage; tree via `ParentId` + `Ordinal`.
- Focus/done mutate WitDB; board chrome `@phase` / `#Product` / `~Executor` — affinity/tag/Who, не status.
- Incomplete leaf (0.5.309+): `pending|active`, skip handoff; leaf = no incomplete children.
- `feature_focus` / `task_focus` / `done` → leaf resolve + `leaf-wake` AutoI (2s).
- REPL: bare `feature X` = upsert/dedupe (+ leaf-arm on dedupe); `feature focus X` → `feature_focus` (0.5.311+).
- `done` / `shipped` with **feature title** (or bare when feature focused, no task) → close incomplete leaves + clear focus (0.5.412).
- `shipped` without prior `start` → implicit wall start (ADX ceremony tax).
- Soft-warn FileLinesWarn=400; `IntentWorkspaceStore` is `partial` by concern (Core/Intent/Stage/Scene/Persist/Find + Leaf/StageCriteria(+Norm)/StageEvents/StageProduct/StageExecutor).
- WitDB path = `StateRoot/{seat}/intent-workspace.witdb` (per-seat; dual seats never share FileShare.None). Primary `cdp` once Moves legacy flat file.
- **All store DB I/O via `WithDb`** (file Mutex + in-proc Lock + transient retry). Never bare `Open()` for Status/Scene*/Stage* — concurrent desk readers race FileShare.None (fixed 0.5.623).
- **Review Results** (operator remarks): `cmd=review <text>|list|ack <id>` (alias remark|rr) — durable stage_events on open leaf; pulse `review×N`; `done`/`shipped` soft-refuse while open (`IdeReviewShield`, force= escape). Dialog stays dialog — agent stamps; dig before Done. SoftOrgan `review files|open` unchanged.

## Entry

- Cockpit `cmd=` / `go=plan`
- `IdeTaskManager.Dispatch` · `Mutations.Feature|Task.*` · `IntentWorkspaceStore.Leaf` · `StageEvents.Review` · `IdeReviewShield`

## Antipatterns

- `StageEventsForStage` empty → `AsEnumerable()` full-table on large WitDB (lived 2026-08-05: `go_detail=full` / mid-turn Connection closed ~172MB). Prefer server Where; escape = SQL `StageId` filter — never load-all.
- `IntentHasStageProduct` → `AsEnumerable()` after IntentId filter (same lived thrash class; #CIDE `feature_done` shield path). Keep `Any` in SQL — never materialize stages for case-fold.
- **BuildBoard ×3 per CallTool** — Handle + CollectWork/PulseLine + PublishGlass each rebuilt board (3× TaskManagerSnapshot / WithDb on ~165MB WitDB → ~20–36s `timeout_wake`). ThreadStatic board cache + Invalidate on mutation (lived 2026-08-05).
- **PublishGlass extra snapshot + all-Stages load** — after board-cache, PublishGlass still called `TaskManagerSnapshot` again; snapshot loaded every Stage row. Fix: glass uses Board titles; stages filtered to ActiveIntentId (lived 2026-08-05 residual).
- Glass form roundtrip for operator remarks when dialog + TM stamp already carry the remark.
- Treating SoftOrgan `go=review` (code-review desk) as Review Results — TM is `review <remark>` / `review list`.
- `feature` with `@phase/#Product` must not dedupe onto a bare-title twin (chrome query → create tagged or match chrome-bearing only) @ 0.5.553+.
- Intent unique-prefix CLOSED @ 0.5.648 — Dig densest SoftFL… stems; truncated title must not FeatureDone/focus onto content twin. Stages keep unique prefix (slash-title seed).
- Asking operator how focus/done works without reading Leaf + Mutations.
- Seeding task titles that contain REPL verbs (`|focus|start`).
- Chaining board verbs with `;` in one `cmd=` (`feature X; task Y; start`) — junk titles; refuse `multi_cmd` @ 0.5.521.
- Re-inlining Intent/Stage/Scene/Persist/Find into one mega-file past soft-warn.
- Treating `wave shipped` as auto-complete rectangle or seeming Done without PNG/domain (`IdeWaveShipShield` @ 0.5.675+).
- Unquoted spaced `evidence=` on `wave shipped` → token split / shield miss (`MergeWaveShipArgs` pathish join @ throughput).
- Treating `done invent Feature…` as "task not found" when feature exists — fixed 0.5.412.

## last_ship
- **2026-08-08 SoftFL Stage.Executor** — operator SoftFL ACCEPT (TM Who so Кир/Sierra don't collide). Densify Product pattern: WitDB `Executor` + ALTER, REPL `executor|assignee`, title `~Who`, board `~Sierra`. Normalize Sierra|Кир|Света. Files-list = existing `dig=` (no invent). Tests StageExecutorTests **9** + StageProductTests **9** = 18/18. SoftOrgan invent REJECT.
- **2026-08-06 done dig= title footgun** — REPL Clock swallowed inline dig=/evidence=/domain= as task title (`task not found: dig=…`). `MergeClockDoneShipArgs` + pathish join; parity with wave shipped. Domain=`iderepl`. SoftFL REJECT.
- **2026-08-06 evidence-path spaces** — REPL `MergeWaveShipArgs` joins pathish keys across spaces (lived Personal Cursor Folder). Tests IdeWaveShipShieldTests (+ Feature_done human_face isolated from live Autoi half-a). Domain=`throughput`.
- **2026-08-06 StageProduct dig-safe** — `IntentHasStageProduct` never `AsEnumerable`: SQL `Any` + `ToUpper` (NormalizeProduct want). Same lived thrash class as StageEvents Connection closed. Dig: StageProduct.cs leftover after StageEvents fix; #CIDE feature_done shield path. Tests StageProductTests (IntentHasStageProduct_matches_without_materialize). Wave FullReady-product-dig-next.
- **2026-08-06 wave shipped teeth** — `IdeWaveShipShield` + `IdeSeemingDoneShield`: pending items refuse; human-faced wave/feature needs evidence PNG + fresh domain stamp; REPL preserves evidence=/domain=. Tests IdeWaveShipShieldTests. Being ≠ seeming habitat enforce.
- **2026-08-05 residual first-snapshot** — PublishGlass no second `TaskManagerSnapshot` (Board.ActiveFeature/StageTitle); `TaskManagerSnapshot` loads Stages only for ActiveIntentId. Test PublishGlass cache-hit. SoftFL BuildBoard lines REJECT.
- **2026-08-05 Board cache pulse-tax** — ThreadStatic `BuildBoard` reuse (intent/stage/phase keys) + `InvalidateBoardCache` on mutation; Handle/PulseLine/PublishGlass share one WitDB snapshot. Test IdeTaskManagerBoardCacheTests. Dogfood: `go=plan` pulse no `timeout_wake` (was ~20–36s). Dual hard deploy build_utc=15:34:18Z. SoftFL method_lines BuildBoard REJECT.
- **2026-08-05 StageEvents dig-safe** — `StageEventsForStage` never `AsEnumerable` full-scan: server Where → SQL `StageId` filter (OutWit empty-Where parity). Lived: dig leaf / `go_detail=full` → Connection closed on ~172MB WitDB. Tests StageEventLedger 3/3. Dig path parity with ship-safe StageClock.
- **2026-08-05 StageEvents ship-safe** — `StageClockShipped` never full-scans events (server Where only + catch); `HealNullStageEventUtc` DELETE NULL Utc; `StageEventsForStage` server-first then client match with heal. Lived: Kill text-hell wall `shipped` blocked by NULL→DateTimeOffset / 172MB AsEnumerable; `done`+force closed leaf; this unblocks wall `shipped`.
- **2026-08-05** — WitDB EF provider **1.0.3 → 12.2.0** (author: fixes for torn free-list / handle leak / GUID filter). Kept Wit seat path `intent-workspace.witdb`. SQLite cutover aborted. Upstream #121–#123 filed; dogfood on 12.2.0.
- **2026-08-05 Review dig fix** — OutWit server `Where(StageId==guid)` empty on durable WitDB; dig/list/review via client StageId match (`StageEventsForStage`). No DROP-heal (Utc corruption). Live dogfood: open≥1, done refuse, ack N-id.
- **2026-08-05 Review Results MLP** — operator remarks on leaf (`review`/`remark`/`rr`) · open dig · ack · done refuse `open_operator_reviews` · SoftOrgan `review files` narrowed · tests IdeTaskManagerReviewTests
- **2026-08-04 SickLeaveNight648 mid** — ignite hygiene + glass DIG REJECT reopen; product dig PathMutate vs Autoi duplex seeded.
- **2026-08-04 invent DIG FindIntent648** — DIG REJECT SoftFL/Meta/Citizen/OOM-eol reopen; DIG ACCEPT densest = throughput-wave idle → SickLeaveNight648 (ignite stale-arms → CIDE/glass citizen15 → product wave).
- **2026-08-04 Dig FindIntent@0.5.648** — DIG REJECT SoftFL/Meta/Citizen reopen; DIG ACCEPT board CLOSED hygiene under FeatureDone@0.5.647 + FindIntent unique-prefix CLOSED; inventory sole gap = throughput-wave idle; BoardClosedHygiene648 1/4 (Meta defer/BATCH/SoftFL peel shipped; Dig FindIntent feature shipped).
- **0.5.648** — FindIntent unique-prefix CLOSED for intents (content-twin Dig densest SoftFL… steal). Test FindIntent_unique_prefix_does_not_steal_content_twin. Stages keep Find.cs unique prefix.
- **2026-08-04 SoftFL CLOSED dig** — DIG REJECT SoftFL reopen (inventory CLOSED). densest ACCEPT: board CLOSED noise hygiene under FeatureDone @0.5.647 live dogfood; IntentSelect clears stage by design (restore in FeatureDone); dual `-Target` terminal habit ≠ code mill.
- **2026-08-04 invent DIG** — SoftFL/Meta/WitDB/Hol/Glass residual/Citizen GREEN CLOSED. densest ACCEPT: FeatureDone always cleared focus on foreign `shipped` (hygiene stole invent dig). SoftFL WARN×4 DIG REJECT reopen. Wave SickLeaveInvent.

- **2026-08-04 DIG REJECT** — post-SoftOrgan densest dig: WitDB torn quarantine already lived @0.5.628 (heal+VL+tm stamp); Hol habit ≠ product organ (throughput list→batch→ship / a×b already flying). SoftFL/Meta peel reopen = regression. Board CLOSED noise → hygiene; densest next ≠ re-mill hosts/peel.
- **0.5.628** — WitDB torn free-list / pageNumber OOR: `WorkspaceDbTornHeal` quarantine `*.torn-*.bak` + EnsureCreated inside `WithDb` (one retry); IdeTaskManager soft-fail wraps BuildBoard (`torn_witdb`). Tests WorkspaceDbTornHealTests. Lived: dual hard · quarantine 1.1GB+39MB seats · `cdp_open`+`go=plan` green · fresh ~220KB/~176KB.
- **0.5.623** — WitDB: Status/SceneList/ScenePark/SceneSwitch/StageEnqueue|Get|Complete|Fail via `WithDb` (was ungated `Open()` → concurrent IOException). Test IntentWorkspaceWithDbGateTests. Lived: `@intent work op=status|scene_list` `ack=4/4`.
- 0.5.553: FindIntent chrome query refuses bare-title twin (survivor seat wrong-board) · 2026-08-03
- 0.5.521: CCL refuse `;` + board-verb chain (`RefuseChainedBoardCmd` / `ChainedTitleHint`) — no junk feature/task titles · VL #27 · 2026-08-02
- Persist FileLines near-miss peel: OpenRecent + StageClock out of Persist.cs (300→105) @ 0.5.454 · 2026-08-02
- Find FileLines near-miss peel: WorkFocus + ScriptLastRun out of Find.cs (335→188) @ 0.5.453 · 2026-08-02
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
