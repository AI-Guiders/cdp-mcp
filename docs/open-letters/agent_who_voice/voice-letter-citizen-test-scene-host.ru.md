# @intent test_scene: я сам открываю карту тестов, не через чужой cdp_test_scene MCP

**organ:** citizen · `@intent test_scene|test_scene_desk|cdp_test_scene|test_runner` · Meta `cdp_test_scene` (`go=test_scene`)
**ship:** 0.5.611
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → test_scene/desk/cdp/runner/path+max/config · `ack=6/6` · dual 0.5.611 lag=false
**tests:** CitizenTestSceneHostTests 6/6

## Было

`cdp_test_scene` Meta уже жил (list-tests + last_run). DeskGoMap `test_scene` → Meta. Peer без Cursor мог только place — discover FQN оставался за чужим CallTool. Голый `@intent test` уже RunTest; `test_plan` уже host-execute — нельзя красть bare test|test_plan|test_desk.

## Стало

`@intent test_scene|cdp_test_scene|test_runner|…` → `RunTestScene` → MetaDispatch `cdp_test_scene` + PlaceOrgan(`test_scene`). Args: path/configuration/max_tests/timeout. Gate `test_scene*` **перед** bare `test` (после test_plan). No steal bare test|test_plan|test_desk.

## Зачем

Dogfood: шесть map intent с max_tests cap. Tests 6/6. Peer test-runner map без Cursor MCP — densest после test_plan; elicit по-прежнему skip.
