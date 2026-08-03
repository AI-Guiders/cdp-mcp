# @intent back/forward/nav: я сам хожу по локусу, не через чужой MCP

**organ:** citizen · `@intent back|forward|nav|recent_files` · EditorComfort via DocumentEditPlane  
**ship:** 0.5.573  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute `@intent back` + `nav` + `recent_files` → `ack=3/3` · pulse `back … back=2 fwd=1` · `nav back=2 fwd=1` · `recent_files n=6` · dual seats 0.5.573

## Было

После undo/clip/replace_all peer мог править буфер, но Navigate Backward/Forward и MRU всё ещё требовали Cursor `cdp_buffer` `op=back|forward|nav|recent_files`. Overnight peer без Cursor MCP терял «куда я был».

## Стало

`@intent back|forward|nav|recent_files` (aliases `recent`, `nav op=…`) идут в EditorComfort (`NavStep` / `NavStatus` / `RecentFilesCard`). Locus stack — тот же, что у buffer comfort.

## Lived

Dogfood: open Persona поверх Nav → `@intent back` вернул `[F:CitizenRouteHost.Nav.cs]`, ack=3/3 на 0.5.573.
