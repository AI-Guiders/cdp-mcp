# @intent share: я сам отдаю оператору на полку, не грузя тело в агент

**organ:** citizen · `@intent share` · IdeShare via DocumentEditPlane  
**ship:** 0.5.577  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent share with=operator path=…/voice-letter-citizen-take-host.ru.md` → `ack=1/1` · pulse `share operator … shared chars=845` · debug seat 0.5.577

## Было

После take peer мог забирать span в свой контекст, но «отдать оператору файл+thin chat» (IdeShare `with=operator|self` / `from=self`) всё ещё требовал Cursor `cdp_buffer` `op=share`.

## Стало

`@intent share` ждёт IdeShare: `with=`/`to=`, `from=self`, `path=`/`body=`/`ask=`/`dir=`.

## Lived

Dogfood: 845 chars shared to `.cdp/share`, ack=1/1 на 0.5.577.
