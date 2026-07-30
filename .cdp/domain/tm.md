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

## Entry

- Cockpit `cmd=` / `go=plan`
- `IdeTaskManager.Dispatch` · `Mutations.Feature|Task.*` · `IntentWorkspaceStore.Leaf`

## Antipatterns

- Asking operator how focus/done works without reading Leaf + Mutations.
- Seeding task titles that contain REPL verbs (`|focus|start`).

## last_ship

- 0.5.309 leaf AutoI — `6a97f48` (2026-07-30)
- 0.5.310 Domain axis + remount domain pulse (pressure checklist + charge appendix)
- 0.5.311 FeatureAdd dedupe leaf-arm + REPL `feature focus X`
