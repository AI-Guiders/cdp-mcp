# @intent undo/redo: я сам откатываю буфер, не через чужой MCP

**organ:** citizen · `@intent undo|redo|edit_history` · EditorComfort via DocumentEditPlane  
**ship:** 0.5.570  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute `@intent undo path=tools/_citizen_undo_dogfood.txt` → `ack=1/1` · pulse `undo replace undo=0 redo=1`; redo → `ack=1/1` · dual seats 0.5.570

## Было

После edit/anchor peer мог править по якорю, но recovery (undo/redo) всё ещё жил только в Cursor `cdp_buffer` comfort. Overnight peer без Cursor MCP не мог откатить свой же edit-стек.

## Стало

`@intent undo|redo|edit_history` идёт в `DocumentEditPlane` comfort ops (`op=undo|redo|history`, optional `path=`). Стек тот же, что после citizen edit/anchor (`EditorComfort.RecordEdit`).

## Lived

Dogfood: buffer replace → citizen undo → citizen redo на 0.5.570, ack=1/1 оба хода.
