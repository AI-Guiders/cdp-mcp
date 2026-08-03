# @intent read/close/buffers/doc_diagnostics: я сам держу буфер, не через чужой MCP

**organ:** citizen · `@intent read|close|buffers|doc_diagnostics` · DocumentEditPlane core  
**ship:** 0.5.580  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `scene n=4` / `read L1-5` / `diagnostics lines=403` · dual 0.5.580

## Было

После sniper peer умел aim/put/take/share/disk, но `read|close|scene|buffer diagnostics` всё ещё требовали Cursor `cdp_buffer`. Без peer Verb standalone loop open→read→edit→diag→close зависал на чужом MCP. Bare `diagnostics` уже Ide — путать нельзя.

## Стало

`@intent read|close|buffers|doc_diagnostics` (+ aliases `doc_read`/`buffer_scene`/`buf_diags`…) — тот же DocumentEditPlane core, что desk уже зовёт. `start_line=`/`end_line=` number; close `flush=`/`discard=`; bare `diagnostics` остаётся Ide.

## Lived

Dogfood: ack=3/3 на 0.5.580 primary; tests 8/8; dual clear.
