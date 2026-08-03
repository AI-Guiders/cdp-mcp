# @intent tools: я сам смотрю shortlist palette, не через чужой cdp_tools MCP

**organ:** citizen · `@intent tools|tools_desk|tools_palette|cdp_tools|palette` · Meta `cdp_tools`
**ship:** 0.5.619
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → tools + desk/cdp/palette + phase/limit + go=tools place · `ack=7/7` · dual 0.5.619 lag=false
**tests:** CitizenToolsHostTests 6/6

## Было

`go=tools` — place-only (после DeskGoMap). Meta `cdp_tools` уже жил (shortlist catalog=f(phase,object[,intent][,language])). Peer без Cursor мог place — palette крутил чужой CallTool. `tools_options` остаётся settings.

## Стало

`@intent tools|cdp_tools|…` → `RunTools` → MetaDispatch `cdp_tools` + PlaceOrgan(`tools`). Args: phase=/object=/intent=/language=/limit=. Pulse `tools · phase/object · n=total · lang?`. **No steal** `go=tools` / `tools_options`.

## Зачем

Dogfood: семь tools intent (A default + query C + place). Tests 6/6. Peer palette without Cursor MCP — densest Meta после session; next dig capabilities.
