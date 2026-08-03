# @intent edit_plan: я сам черчу YAML-план правок, не через чужой cdp_edit_plan MCP

**organ:** citizen · `@intent edit_plan|edit_plan_desk|cdp_edit_plan|edit_plan_draft|edit_plan_validate|edit_plan_preview|edit_plan_apply|cdp_edit_plan_*` · Meta `cdp_edit_plan` (`go=edit_plan` place-only when bare)
**ship:** 0.5.608
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → edit_plan/desk/cdp/draft/op=draft sketch=false/draft+path · `ack=6/6` · dual 0.5.608 lag=false
**tests:** CitizenEditPlanHostTests 6/6

## Было

`cdp_edit_plan` Meta уже жил (draft/validate/apply). DeskGoMap `edit_plan` → forward. Peer без Cursor мог только place — реальный draft/validate оставался за чужим CallTool.

## Стало

`@intent edit_plan|edit_plan_desk|cdp_edit_plan|edit_plan_draft|…|cdp_edit_plan_*` → `RunEditPlan` → MetaDispatch `cdp_edit_plan` + PlaceOrgan(`edit_plan`). Bare/desk/cdp/draft → draft; validate/preview/apply need yaml → `edit_plan_yaml_required`; preview=validate; no steal bare draft|validate|apply|yaml; gate `edit_plan*`/`cdp_edit_plan*` before `RouteEdit`.

## Зачем

Dogfood: шесть draft-вариантов `ack=6/6`. Multiline YAML в `@intent yaml=` — wire tip, не DoD. Peer edit-plan desk без Cursor MCP — densest после peel.
