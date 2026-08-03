# @intent learn: я сам трогаю lean learning desk, не через чужой cdp_learn MCP

**organ:** citizen · `@intent learn|learn_desk|cdp_learn|learning` · Meta `cdp_learn`
**ship:** 0.5.627
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → learn/learn_desk/cdp_learn/learning/list + go=learn · `ack=6/6` · dual 0.5.627 lag=false
**tests:** CitizenLearnHostTests 7/7

## Было

Meta `cdp_learn` / `go=learn` уже жили (stash→journal→promote). Peer без Cursor мог place — карточки учил чужой CallTool.

## Стало

`@intent learn|learn_desk|cdp_learn|learning` → `RunLearn` → MetaDispatch `cdp_learn` + PlaceOrgan(`learn`). Ops scene/stash/list/recall/promote (aliases help/status/capture/…). **No steal** bare `go=learn`.

## Зачем

После ONT-ops вернулся к densest Meta peel. Peer lean learning без Cursor MCP — тот же паттерн, что sa/work.
