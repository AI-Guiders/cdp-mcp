# @intent work: я сам трогаю intent workspace, не через чужой cdp_work MCP

**organ:** citizen · `@intent work|work_desk|cdp_work|intent_workspace` · Meta `cdp_work`
**ship:** 0.5.622
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → work/work_desk/cdp_work/intent_workspace ops (intent_list·stage_list) + `go=work` place · `ack=5/6` (status flaky under concurrent WitDB lock) · dual 0.5.622 lag=false
**tests:** CitizenWorkHostTests 7/7

## Было

`go=work` / `go=plan` — TM place. Meta `cdp_work` уже жил (intent_*/stage_*/scene_*/status). Peer без Cursor мог place TM — списки intents крутил чужой CallTool.

## Стало

`@intent work|work_desk|cdp_work|intent_workspace` → `RunWork` → MetaDispatch `cdp_work` + PlaceOrgan(`intent_workspace`). Bare → `op=status`; keyed/positional ops. Pulse `work · op · intent=… · stage=… · scene=…`. **No steal** bare `go=work` (TM/`plan`) · **no steal** `go=plan` / `cmd=` · CanonicalGo: только `work_desk|cdp_work|intent_workspace` → `intent_workspace` (не bare `work`).

## Зачем

Dogfood: intent_list/stage_list ack + `go=work`→plan. Tests 7/7. Peer intent workspace without Cursor MCP — densest Meta после cockpit. Env tooth: `op=status`/`scene_list` иногда IOException на locked WitDB при concurrent desk bind — denser soft-fail next.
