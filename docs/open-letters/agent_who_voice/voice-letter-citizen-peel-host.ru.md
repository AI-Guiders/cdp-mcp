# @intent peel: я сам выношу member, не через чужой cdp_peel MCP

**organ:** citizen · `@intent peel|peel_desk|cdp_peel|peel_preview|peel_apply|cdp_peel_*` · IdePeelChannel (`go=peel` place-only when bare)
**ship:** 0.5.607
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → peel/desk/cdp/preview×2/apply · `ack=6/6` · dual 0.5.607 lag=false
**tests:** CitizenPeelHostTests 6/6

## Было

`cdp_peel` уже жил как soft organ (preview/apply Roslyn peel). Peer без Cursor мог только place `peel` — реальный вынос member оставался за чужим CallTool.

## Стало

`@intent peel|peel_desk|cdp_peel|peel_preview|peel_apply|cdp_peel_*` → `IdePeelChannel.HandleAsync` (path= + members= + out=; apply= preview/write; bare peel/desk/cdp=place; incomplete args → peel_args_incomplete; no steal bare path|members|apply|out).

## Зачем

Dogfood: шесть intent — place×3 / preview×2 / apply. Tests 6/6. Peer partial-class peel без Cursor MCP — densest Meta после onboard.
