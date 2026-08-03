# @intent browser: я сам хожу в сеть, не через чужой Browser MCP

**organ:** citizen · `@intent browser|internet_browser|web|lynx` · InternetBrowserHabitat
**ship:** 0.5.586  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `browser scene ok … lynx` / `browser which ok` / `browser search ok …duckduckgo…` · dual 0.5.586

## Было

После Ide tip chase peer мог dig/edit/build без Cursor, но интернет оставался place-only на M:browser. Без Cursor Browser/WebSearch peer слеп к миру снаружи.

## Стало

`@intent browser|internet_browser|web|lynx` → `InternetBrowserHabitat.Dispatch`. Open/search/follow требуют url=/q=/link=. Bare `search`/`open`/`close` не краду — остаются find/buffer.

## Lived

Dogfood: ack=3/3 на 0.5.586 primary; tests CitizenBrowserHostTests 7/7; dual clear.
