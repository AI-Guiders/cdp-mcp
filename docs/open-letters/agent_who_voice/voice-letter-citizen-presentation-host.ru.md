# @intent cide_presentation: я сам кручу glass latch, не через чужой cdp_cide_presentation MCP

**organ:** citizen · `@intent cide_presentation|presentation|presentation_*` · `IdeCidePresentationChannel` (`cdp_cide_presentation`)
**ship:** 0.5.596
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → scene×4 / set topology / set tier · `ack=6/6` · dual 0.5.596 lag=false

## Было

Glass presentation уже жила как Meta (`cdp_cide_presentation` scene|get|set → latch). Peer без Cursor мог place organ — topology/tier/instruments оставались за чужим CallTool. Не путать с `cdp_settings` (Tools→Options).

## Стало

`@intent cide_presentation|presentation|cide_presentation_scene|cide_presentation_set|cide_presentation_get|presentation_set|presentation_scene` → `IdeCidePresentationChannel.HandleJson`. Bare `cide_presentation`/`presentation` = scene. UX: `presentation_set topology=(P)(F)(M)` · `cide_presentation set tier=cockpit`. Не ворует bare `set`/`get`/`settings`.

## Зачем

Dogfood: шесть intent — scene×4 + set topology + set tier; все applied. Tests CitizenPresentationHostTests 5/5. Peer трогает CIDE glass latch без Cursor `cdp_cide_presentation`.
