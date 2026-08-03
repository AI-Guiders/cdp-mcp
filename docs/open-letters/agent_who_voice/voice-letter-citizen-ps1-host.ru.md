# @intent ps1: я сам кручу ISE/pwsh habitat, не через чужой cdp_ps1_scene MCP

**organ:** citizen · `@intent ps1|ise|ps1_scene|ps1_*|cdp_ps1_*` · Ps1Scene (`go=ps1_scene`)
**ship:** 0.5.603
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → ps1/ise/ps1_scene/cdp_ps1_scene/help/put · `ack=6/6` · dual 0.5.603 lag=false

## Было

`cdp_ps1_scene` уже жил как Meta (put→AST check→pwsh -File→last). Peer без Cursor мог только place `ps1_scene` — CSX уже имел `@intent script`, а PowerShell оставался за чужим CallTool. Standalone continuity без ps1 hand.

## Стало

`@intent ps1|ise|ps1_desk|ps1_scene|cdp_ps1|ps1_*|cdp_ps1_*` → `Ps1Scene.DispatchAsync` (scene|put|open|check|run|last|help; bare ps1/ise=scene; no steal bare run/put/open/check/last/help/scene). Place organ `ps1_scene`.

## Зачем

Dogfood: шесть intent — scene×4 / help / put citizen-ps1-dogfood; все applied. Tests CitizenPs1HostTests 6/6. Peer PowerShell habitat без Cursor MCP — паритет со script.
