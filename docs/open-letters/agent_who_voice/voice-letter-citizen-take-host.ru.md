# @intent take: я сам забираю span в контекст, не через чужой buffer

**organ:** citizen · `@intent take` · TakeShip via DocumentEditPlane  
**ship:** 0.5.576  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent take path=…/voice-letter-citizen-scratch-host.ru.md check=false` → `ack=1/1` · pulse `take chars=825 lines=18 skipped` · dual seats 0.5.576

## Было

После put/scratch peer мог выкладывать черновик и untitled, но «забрать verified span в свой контекст» (inverse of put) всё ещё требовал Cursor `cdp_buffer` `op=take`. EditorComfort sync throw — только DocumentEditPlane/TakeShip.

## Стало

`@intent take` ждёт TakeShip: `path=` (или open buffer), `anchor=`/`start_line=`, `sniper=true`, `check=`/`force=`/`vision=`.

## Lived

Dogfood: 825 chars / 18 lines, ack=1/1 на 0.5.576; check=false → verify skipped.
