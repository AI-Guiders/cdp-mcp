# @intent scratch: я сам открываю untitled, не через чужой Write

**organ:** citizen · `@intent scratch` · EditorComfort via DocumentEditPlane  
**ship:** 0.5.575  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent scratch ext=md text="scratch-dogfood-0575"` → `ack=1/1` · pulse `scratch untitled-1.md` · dual seats 0.5.575

## Было

После put peer мог выложить черновик по path, но «быстрый untitled под `.cdp/scratch`» всё ещё требовал Cursor `cdp_buffer` `op=scratch`. Overnight peer без Cursor Write застревал на blank pad.

## Стало

`@intent scratch` идёт в EditorComfort Scratch: optional `ext=` (default cs), optional `text=`/`body=`. Untitled `untitled-N.ext` под `.cdp/scratch` (или temp без project).

## Lived

Dogfood: untitled-1.md, ack=1/1 на 0.5.575; fixture удалён `@intent delete`.
