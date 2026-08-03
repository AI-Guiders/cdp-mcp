# @intent replace_all: я сам меняю все вхождения, не через чужой MCP

**organ:** citizen · `@intent replace_all` · EditorComfort via DocumentEditPlane  
**ship:** 0.5.572  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute `@intent replace_all path=… query=foo text=bar` → `ack=1/1` · pulse `replace_all n=2` · dual seats 0.5.572

## Было

После undo/clip peer мог откатить и носить фрагменты, но bulk rename в буфере всё ещё требовал Cursor `cdp_buffer` `replace_all`. Overnight peer без Cursor MCP застревал на массовой правке.

## Стало

`@intent replace_all` (перед PathMutate `replace`) идёт в EditorComfort (`query=`/`old=` + `text=`/`new=`, optional `regex=`/`ignore_case=`). Один undo-step на весь проход.

## Lived

Dogfood: fixture `foo×2` → `bar×2`, ack=1/1, n=2 на 0.5.572.
