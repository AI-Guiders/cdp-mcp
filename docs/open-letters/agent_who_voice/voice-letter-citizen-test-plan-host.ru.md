# @intent test_plan: я сам планирую/прогоняю тесты, не через чужой cdp_test_plan MCP

**organ:** citizen · `@intent test_plan|test_plan_desk|cdp_test_plan|test_plan_preview|test_plan_apply|test_plan_draft|test_plan_run|cdp_test_plan_*` · Meta `cdp_test_plan` (`go=test_plan`)
**ship:** 0.5.610
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → test_plan/desk/cdp/preview+filter/draft/op=preview failed_first · `ack=6/6` · dual 0.5.610 lag=false
**tests:** CitizenTestPlanHostTests 6/6

## Было

`cdp_test_plan` Meta уже жил (op=preview|apply; filter/failed_first). DeskGoMap `test_plan` → Meta. Peer без Cursor мог только place — select+run оставался за чужим CallTool. Голый `@intent test` уже был lifecycle RunTest — нельзя было красть bare `test`.

## Стало

`@intent test_plan|cdp_test_plan|test_plan_preview|…` → `RunTestPlan` → MetaDispatch `cdp_test_plan` + PlaceOrgan(`test_plan`). Bare/desk/cdp/draft → preview; draft→preview; run→apply; gate `test_plan*` **перед** bare `test`. No steal bare test|preview|apply|draft|run.

## Зачем

Dogfood: шесть preview-heavy intent. Tests 6/6. Peer test-plan desk без Cursor MCP — densest после analysis; elicit Cursor-spike по-прежнему skip.
