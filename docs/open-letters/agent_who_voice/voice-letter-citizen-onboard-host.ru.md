# @intent onboard: я сам сканирую ProjectRoot, не через чужой cdp_onboard MCP

**organ:** citizen · `@intent onboard|onboard_desk|explore_desk|explore|cdp_onboard|onboard_*` · IdeOnboardChannel (`go=onboard_desk`)
**ship:** 0.5.606
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → onboard/desk/explore/cdp/scan/clear · `ack=6/6` · dual 0.5.606 lag=false
**tests:** CitizenOnboardHostTests 6/6

## Было

`cdp_onboard` уже жил как soft organ (scene|scan|clear) — cold-start map ProjectRoot. Peer без Cursor мог только place `onboard_desk` — scan оставался за чужим CallTool.

## Стало

`@intent onboard|onboard_desk|explore_desk|explore|cdp_onboard|onboard_*|cdp_onboard_*` → `IdeOnboardChannel.HandleJson` (scene|scan|clear; refresh|rescan→scan; bare onboard/explore=scene; no steal bare scene|scan|clear|refresh). Place organ `onboard_desk`.

## Зачем

Dogfood: шесть intent — scene×4 / scan / clear. Tests 6/6. Peer cold-start map без Cursor MCP — densest Meta после files.
