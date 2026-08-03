# @intent reload/keep_disk/disk_peek: я сам чиню drift, не через чужой buffer

**organ:** citizen · `@intent reload|keep_disk|disk_peek` · DocumentEditPlane disk hygiene  
**ship:** 0.5.578  
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `disk_peek n=0` / `reload n=0` / `keep_disk n=0` · debug seat 0.5.578

## Было

После share peer закрыл put/take/share, но soft-refuse на material drift всё ещё учил `reload|keep_disk` через Cursor `cdp_buffer`. Без peer Verb overnight drift recovery зависал на чужом MCP.

## Стало

`@intent reload|keep_disk|disk_peek` (optional `path=` / `pad=` as number) — тот же DocumentEditPlane triad, что QRH уже называет.

## Lived

Dogfood: ack=3/3 на 0.5.578; pad string→number fix в host BuildDiskArgs + IntOrNull string parse.
