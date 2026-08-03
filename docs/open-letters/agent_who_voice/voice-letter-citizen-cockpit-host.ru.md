# @intent cockpit: я сам смотрю desk pulse, не через чужой cdp_cockpit MCP

**organ:** citizen · `@intent cockpit|cockpit_desk|cdp_cockpit|agent_desk` · Meta `cdp_cockpit`
**ship:** 0.5.621
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → cockpit + desk/cdp/agent + layout+pane_full + go=cockpit place · `ack=6/6` · dual 0.5.621 lag=false
**tests:** CitizenCockpitHostTests 7/7

## Было

`go=cockpit` — place-only (после DeskGoMap). Meta `cdp_cockpit` уже жил (seats/view/alert). Peer без Cursor мог place — desk pulse крутил чужой CallTool. `cockpit_host` = Glass GUI, другой verb.

## Стало

`@intent cockpit|cdp_cockpit|…` → `RunCockpit` → MetaDispatch `cdp_cockpit` + PlaceOrgan(`cockpit`). Args: `layout=` / `pane_full=` / `go_detail=` / `desk_detail=` / `locus=`. Pulse `cockpit · mode=… · seats=n · sa …`. **No steal** `go=cockpit` · **no steal** `cockpit_host|cockpit_start|cockpit_stop`.

## Зачем

Dogfood: cockpit intent + place. Tests 7/7. Peer where-am-I desk without Cursor MCP — densest hub Meta после capabilities.
