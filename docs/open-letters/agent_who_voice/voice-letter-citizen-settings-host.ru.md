# @intent settings|options: я сам хожу в Tools→Options, не через чужой settings MCP

**organ:** citizen · `@intent settings|options|prefs|languages|lsp_*` · MetaDispatch `cdp_settings`
**ship:** 0.5.593
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → settings/options/page/languages/get/which · `ack=6/6` · dual 0.5.593 lag=false

## Было

`cdp_settings` уже жил как Meta Tools→Options (ADR 0190). Peer без Cursor мог place organ — options/page/get оставались за чужим CallTool.

## Стало

`@intent settings|options|prefs|ide_settings|tools_options|languages|settings_*|lsp_*` → `MetaDispatchResolver("cdp_settings")`. Bare `settings`/`options` = options; bare `languages` = page languages. UX: `settings page page=desk` · `settings get key=` · `settings set key= value=` · `lsp_probe id=`. Не ворует bare `get`/`set`/`page`/`mcp`/`shell`.

## Lived

Dogfood: шесть intent — options×2, page desk, languages, get, which; все applied. Tests CitizenSettingsHostTests 6/6. Peer трогает Options без Cursor `cdp_settings`.
