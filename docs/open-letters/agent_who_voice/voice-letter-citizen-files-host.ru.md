# @intent files: я сам брожу по дереву, не через чужой cdp_files MCP

**organ:** citizen · `@intent files|files_desk|cdp_files|fm|files_*|cdp_files_*` · IdeFilesChannel (`go=files_desk`)
**ship:** 0.5.605
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → files/files_desk/cdp_files/list/tree/open · `ack=6/6` · dual 0.5.605 lag=false
**tests:** CitizenFilesHostTests 7/7 (depth= as Number)

## Было

`cdp_files` уже жил как Meta (scene|list|cd|up|stat|tree|open|text|search|roots|clear) — agent File Manager. Peer без Cursor мог только place `files_desk` — FS browse оставался за чужим CallTool. Standalone continuity без files hand.

## Стало

`@intent files|files_desk|cdp_files|file_manager|fm|files_*|cdp_files_*` → `IdeFilesChannel.Handle` (scene|list|cd|up|stat|tree|open|text|search|roots|clear; ls|dir→list; dump|read→text; find→search only in files context; bare files=scene; no steal bare list/open/search/find/read/cd/scene/clear). Place organ `files_desk`.

## Зачем

Dogfood: шесть intent — scene×3 / list / tree / open; все applied. Tests CitizenFilesHostTests 6/6. Peer File Manager без Cursor MCP — densest Meta после icm.
