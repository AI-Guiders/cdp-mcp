# @intent session: я сам трогаю session plane, не через чужой cdp_session MCP

**organ:** citizen · `@intent session|session_desk|session_plane|cdp_session` · Meta `cdp_session`
**ship:** 0.5.618
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → session + desk/cdp/plane + include_pack + go=session place · `ack=6/6` · dual 0.5.618 lag=false
**tests:** CitizenSessionHostTests 6/6

## Было

`go=session` — place-only. Meta `cdp_session` уже жил (omnibus context+shortlist+health+continuity; `include_pack=`). Peer без Cursor мог place — plane крутил чужой CallTool. `session_context` остаётся context host.

## Стало

`@intent session|cdp_session|…` → `RunSession` → MetaDispatch `cdp_session` + PlaceOrgan(`session`). Args: include_pack=/pack=. Pulse `session · phase/object · A|pack`. **No steal** `go=session` / `session_context`.

## Зачем

Dogfood: шесть session intent (A default + pack C + place). Tests 6/6. Peer omnibus without Cursor MCP — densest Meta после quality soft-organ; next dig tools/capabilities.
