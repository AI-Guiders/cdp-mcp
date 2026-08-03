# @intent copy/cut/paste: я сам держу clipboard, не через чужой MCP

**organ:** citizen · `@intent copy|cut|paste|clipboard` · EditorComfort via DocumentEditPlane  
**ship:** 0.5.571  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent copy text=…` + `@intent clipboard` → `ack=2/2` · pulse `copy c1 chars=20` / `clipboard frames=1` · dual seats 0.5.571

## Было

После undo peer мог откатить edit-стек, но перенос фрагментов (copy/cut/paste) всё ещё жил только в Cursor `cdp_buffer` comfort. Overnight peer без Cursor MCP не мог двигать spans между буферами.

## Стало

`@intent copy|cut|paste|clipboard|clip_clear` идёт в тот же EditorComfort (`text=` / `anchor=` / `frame=` / `place=`). Стек клипов — SessionClipboard, как у buffer comfort.

## Lived

Dogfood: copy text= → frame c1 → clipboard frames=1 на 0.5.571, ack=2/2.
