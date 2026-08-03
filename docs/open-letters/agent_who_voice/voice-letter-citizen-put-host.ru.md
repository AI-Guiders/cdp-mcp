# @intent put: я сам выкладываю черновик, не через чужой Write

**organ:** citizen · `@intent put` · EditorComfort via DocumentEditPlane  
**ship:** 0.5.574  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent put path=tools/_put-dogfood-0574.txt text="put-dogfood-ok"` → `ack=1/1` · pulse `put create chars=14` · dual seats 0.5.574

## Было

После undo/clip/replace_all/nav peer мог править и ходить по локусу, но «выложить черновик одним выстрелом» (Cursor Write analogue) всё ещё требовал Cursor `cdp_buffer` `op=put` или create. Overnight peer без Cursor Write застревал на dump.

## Стало

`@intent put` идёт в EditorComfort Put: `path=` dump (`overwrite=`), `anchor=`+`place=`, `sniper=true`, или `frame=` из clipboard. Один undo-step на dump.

## Lived

Dogfood: create draft 14 chars, ack=1/1 на 0.5.574; fixture удалён `@intent delete`.
