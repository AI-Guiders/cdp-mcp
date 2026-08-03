# @intent cockpit_host: я сам поднимаю Glass, не через чужой Meta CallTool

**organ:** citizen · `@intent cockpit_host|cockpit_start|cockpit_stop` · `IdeCockpitHostChannel` (`cdp_cockpit_host`)
**ship:** 0.5.598
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → scene×3 / start / scene-up / stop · `ack=6/6` · dual 0.5.598 lag=false

## Было

Glass GUI Start/Stop уже жил как Meta (`cdp_cockpit_host` / `go=cockpit_start|cockpit_stop`). Peer без Cursor мог только place organ — start/stop оставались за чужим CallTool. Без этого Standalone chain не открывает operator console.

## Стало

`@intent cockpit_host|cdp_cockpit_host|cockpit_start|cockpit_stop|cockpit_host_*` → `IdeCockpitHostChannel.HandleJson` (scene|start|stop; bare cockpit_host=scene; compounds cockpit_start/stop). UX: `cockpit_start path=…` · `cockpit_host op=scene` · `cockpit_stop`. Не ворует bare `start`/`stop`/`open`/`close`.

## Зачем

Dogfood: шесть intent — scene×3 (down), start (pid+latches), scene (up), stop (down); все applied. Tests CitizenCockpitHostHostTests 5/5. Peer поднимает dual-cockpit Glass без Cursor `cdp_cockpit_host`.
