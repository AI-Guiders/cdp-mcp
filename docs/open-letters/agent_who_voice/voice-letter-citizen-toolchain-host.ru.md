# @intent toolchain: я сам проверяю PATH, не через чужой cdp_toolchain MCP

**organ:** citizen · `@intent toolchain|toolchain_*` · `IdeToolchainChannel` (`cdp_toolchain`)
**ship:** 0.5.597
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → scene×2 / probe×2 / ensure python×2 · `ack=6/6` · dual 0.5.597 lag=false

## Было

Toolchain уже жил как soft organ Meta (`cdp_toolchain` scene|probe|ensure|install|add|which). Peer без Cursor мог place organ — ensure/probe оставались за чужим CallTool. Не путать с `lsp_ensure` (другая ось).

## Стало

`@intent toolchain|toolchain_desk|cdp_toolchain|toolchain_ensure|toolchain_probe|toolchain_install|toolchain_add|toolchain_scene|toolchain_which` → `IdeToolchainChannel.HandleJson`. Bare `toolchain` = scene. UX: `toolchain ensure id=python` · `toolchain_probe` · `toolchain which id=go`. Не ворует bare `ensure`/`probe`; `lsp_ensure` остаётся settings.

## Зачем

Dogfood: шесть intent — scene×2, probe×2, ensure python already_ok×2; все applied. Tests CitizenToolchainHostTests 5/5. Peer трогает DAL-adjacent PATH toolchain без Cursor `cdp_toolchain`.
