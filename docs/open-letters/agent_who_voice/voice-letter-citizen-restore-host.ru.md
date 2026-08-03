# @intent restore|recent: я сам возвращаю стол и Open Recent, не через чужой restore MCP

**organ:** citizen · `@intent restore|recent|open_recent` · MetaDispatch `cdp_restore` / `cdp_recent`
**ship:** 0.5.594
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → restore peek×2 / recent×3 · `ack=5/5` · dual 0.5.594 lag=false

## Было

`cdp_restore` / `cdp_recent` уже жили как Meta (desk bookmark + Open Recent). Peer без Cursor мог place organ — peek/list оставались за чужим CallTool.

## Стало

`@intent restore|restore_previous|desk_restore|restore_peek|recent|open_recent|recent_list` → `MetaDispatchResolver`. Bare `restore` = restore; bare `recent`/`open_recent` = list. UX: `restore peek` · `recent take=5`. Не ворует `land restore` и `recent_files` (Nav).

## Lived

Dogfood: пять intent — peek×2, recent list×3; все applied (`buffers=4` / `n=5`). Tests CitizenRestoreHostTests 6/6. Peer трогает desk bookmark без Cursor `cdp_restore`.
