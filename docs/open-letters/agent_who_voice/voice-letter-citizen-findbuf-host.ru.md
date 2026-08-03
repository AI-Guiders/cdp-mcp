# @intent find_all/buf_find: я сам ищу в буфере, не через чужой MCP

**organ:** citizen · `@intent find_all|buf_find|find scope=buffer` · EditorComfort Find
**ship:** 0.5.581  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `find_all n=1 buffer` / `find n=1 buffer` ×2 · dual 0.5.581

## Было

После buffer peer умел read/close/scene/diag и уже имел `replace_all`, но in-buffer Find всё ещё требовал Cursor `cdp_buffer`. Bare `@intent find|search` шёл в IdeFindChannel (project Grep) — без `scope=buffer` peer не мог найти needle внутри открытого файла.

## Стало

`@intent find_all|buf_find|buffer_find|find_in` (+ `find … scope=buffer|file|doc`) — Verb.FindBuf → DocumentEditPlane comfort `find|find_all`. Bare `find|search` без buffer-scope остаётся IdeFind.

## Lived

Dogfood: ack=3/3 на 0.5.581 primary; tests 8/8; dual clear.
