# Voice Letter #184 — tool= arg ≠ Op · need task_id=/content= · finding_check advice=

**organ:** citizen · RouteKb positional tool · TipKbArgException generic · AppendKbFindingCheckBits  
**lived:** 2026-08-06 · SoftFL invent after `failure_record tool=cdp_test` → unknown · bare failed · thin check

После VL#183 densest residual dig (live wire):
- `failure_record tool=cdp_test` → keyed `tool=` stole Op → `cdp_test unknown`
- `task_upsert` / `write_card` → bare `failed` (`task_id` / `content` is required)
- `finding_check path=` → thin pulse without advice

Теперь: positional known tool wins over tool= ARG · generic `need {name}=` · pulse `advice=` + `path=` + hash. SoftFL invent REJECT. Не Hold.

**live dogfood** dual hard `0.5.675` `build_utc=2026-08-06T19:23:55Z`:
- `failure_record tool=cdp_test` → `failure_record ok` (Op kept; not unknown)
- `task_upsert` → `need task_id=`
- `write_card relative_path=…` → `need content=`
- `finding_check path=…` → `advice=no_memo path=CitizenRouteHost.Kb.cs`
