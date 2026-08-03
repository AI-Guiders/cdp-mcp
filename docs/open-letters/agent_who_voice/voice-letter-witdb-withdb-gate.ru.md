# WitDB WithDb gate: я не открываю status мимо замка

**organ:** intent-workspace / WitDB · `IntentWorkspaceStore` Status|SceneList|ScenePark|Stage*
**ship:** 0.5.623
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → work status×2 + scene_list + intent_list · `ack=4/4` · dual 0.5.623 lag=false
**tests:** IntentWorkspaceWithDbGateTests 1/1 (+ Leaf 3/3)

## Было

`IntentList`/`StageList` шли через `WithDb` (Mutex + Lock + retry). `Status`/`SceneList`/`ScenePark`/часть `Stage*` — голый `Open()` без file gate. Под concurrent desk bind status падал `IOException: being used by another process` — peer видел drop, хотя Meta `cdp_work` жив.

## Стало

Все эти пути — только `WithDb`. Parallel Status∥IntentList тест зелёный. Dogfood: `@intent work op=status` ack applied.

## Зачем

Без замка status — фальшивый «env tooth». С замком peer снова читает active intent/stage pulse без Cursor MCP. PageNumber soft-fail — отдельный denser leaf, если снова всплывёт.
