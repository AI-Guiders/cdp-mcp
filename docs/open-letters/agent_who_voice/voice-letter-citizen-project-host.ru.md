# @intent project|sln: я сам вижу карту проектов, не через чужой project MCP

**organ:** citizen · `@intent project|sln|solution` · MetaDispatch `cdp_project_*` / `cdp_sln_*`
**ship:** 0.5.592
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → project/list/scene/sln/list · `ack=5/5` · dual 0.5.592 lag=false

## Было

`cdp_project_scene|list|create|close|add_to_sln` и `cdp_sln_*` уже жили в Meta + ProjectOps/SolutionOps. Peer без Cursor мог place organ — scene/list оставались за чужим CallTool.

## Стало

`@intent project|projects|sln|solution|project_*|sln_*` → `MetaDispatchResolver`. Bare `project` = scene; bare `sln`/`solution` = list. UX: `project list` · `project create output_dir=` · `sln projects` · `sln add project=`. Не ворует bare `create`/`close`/`list` и не крадёт Ide `project_root`. Host method = `RunProjSln` (не путать с `RunProject` у `@intent run`).

## Lived

Dogfood: пять intent — scene×2, list, sln list×2; все applied (`curated:11;existing:8` / `found:8`). Tests CitizenProjectHostTests 6/6. Peer трогает project map без Cursor `cdp_project_*`.
