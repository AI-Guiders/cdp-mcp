# @intent capabilities: я сам смотрю mounted domains, не через чужой cdp_capabilities MCP

**organ:** citizen · `@intent capabilities|capabilities_desk|cdp_capabilities|caps` · Meta `cdp_capabilities`
**ship:** 0.5.620
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → capabilities + desk/cdp/caps + go=capabilities place · `ack=5/5` · dual 0.5.620 lag=false
**tests:** CitizenCapabilitiesHostTests 5/5

## Было

`go=capabilities` — place-only (после DeskGoMap). Meta `cdp_capabilities` уже жил (domains/affordances/layers). Peer без Cursor мог place — inventory крутил чужой CallTool.

## Стало

`@intent capabilities|cdp_capabilities|…` → `RunCapabilities` → MetaDispatch `cdp_capabilities` + PlaceOrgan(`capabilities`). Args: none. Pulse `capabilities · domains=n · aff=m · list=?`. **No steal** `go=capabilities`.

## Зачем

Dogfood: capabilities intent + place. Tests 5/5. Peer domain inventory without Cursor MCP — densest Meta после tools palette.
