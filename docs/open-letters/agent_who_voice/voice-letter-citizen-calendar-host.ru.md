# @intent calendar: я сам вижу местные сутки, не через чужой clock MCP

**organ:** citizen · `@intent calendar|clock` · IdeCalendarChannel  
**ship:** 0.5.589  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → calendar/pulse/month/clock · `ack=4/4` · dual 0.5.589 lag=false

## Было

Host-local clock уже жил в cockpit `clock=` и soft organ `cdp_calendar` / `go=calendar`. Peer без Cursor мог только place organ — month grid и pulse deadlines оставались за чужим MCP.

## Стало

`@intent calendar|clock|calendar_desk` → `IdeCalendarChannel.Handle` (scene|pulse|month; aliases a/clock/local→pulse, grid→month). `go=calendar*` — place-only. Bare `clock` = scene (не ворует чужие руки).

## Lived

Dogfood: четыре intent на одном turn — scene/pulse/month/clock, все applied с pulse local daypart + TZ. Tests CitizenCalendarHostTests 6/6. Дедлайны sick_leave_dense / citizen_chain видны в том же органе, что и month grid — без угадайки «утро/ночь» из тона чата.
