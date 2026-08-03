# @intent icm: я сам открываю command module, не через чужой cdp_icm MCP

**organ:** citizen · `@intent icm|icm_desk|cdp_icm|command_module|icm_*|cdp_icm_*` · IdeIcmChannel (`go=icm_desk`)
**ship:** 0.5.604
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → icm/icm_desk/cdp_icm/aliases/resolve/invoke · `ack=6/6` · dual 0.5.604 lag=false

## Было

`cdp_icm` уже жил как Meta (scene|aliases|resolve|invoke) — GUI client discovery для standalone. Peer без Cursor мог только place `icm_desk` — command map оставался за чужим CallTool. Standalone continuity без icm hand.

## Стало

`@intent icm|icm_desk|cdp_icm|command_module|icm_*|cdp_icm_*` → `IdeIcmChannel.HandleJsonAsync` (scene|aliases|resolve|invoke; list|map→aliases; exec|run→invoke only in icm context; bare icm=scene; no steal bare run/list/aliases/resolve/invoke/scene). Place organ `icm_desk`.

## Зачем

Dogfood: шесть intent — scene×3 / aliases / resolve plan / invoke cdp_health; все applied. Tests CitizenIcmHostTests 6/6. Peer GUI command discovery без Cursor MCP — densest Meta после ps1.
