# @intent intercom: я сам говорю в Intercom, не через чужой cdp_intercom MCP

**organ:** citizen · `@intent intercom|intercom_*` · `IdeCideIntercomChannel` (`cdp_intercom`)
**ship:** 0.5.595
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → scene×2 / history / presence / send / ack · `ack=6/6` · dual 0.5.595 lag=false

## Было

Intercom уже жил как Meta (`cdp_intercom` scene|send|ack|history|presence). Peer без Cursor мог place organ — голос и presence оставались за чужим CallTool.

## Стало

`@intent intercom|cide_intercom|intercom_send|intercom_scene|intercom_ack|intercom_history|intercom_presence|intercom_inbox` → `IdeCideIntercomChannel.HandleJson`. Bare `intercom` = scene. UX: `intercom send to=pm body="…"` · `intercom presence seat=pf state=busy` · `intercom history limit=10`. Не ворует bare `send`/`ack`/`history`/`presence`/`status`.

## Lived

Dogfood: шесть intent — scene×2, history, presence idle, send «peer intercom host 0.5.595», ack; все applied. Tests CitizenIntercomHostTests 5/5. Peer трогает dual-cockpit голос без Cursor `cdp_intercom`.
