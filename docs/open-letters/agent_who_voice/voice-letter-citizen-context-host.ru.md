# @intent context: я сам кручу phase/object, не через чужой cdp_context MCP

**organ:** citizen · `@intent context|context_desk|cdp_context|session_context` · Meta `cdp_context`
**ship:** 0.5.616
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → context + desk/cdp/session + get + set+layout_hold + go=context place · `ack=7/7` · dual 0.5.616 lag=false
**tests:** CitizenContextHostTests 7/7

## Было

`go=context` — place-only Verb.Go. Meta `cdp_context` уже жил (phase/object/intent/language/get/layout_hold). Peer без Cursor мог place — сессию крутил чужой CallTool.

## Стало

`@intent context|cdp_context|…` → `RunContext` → MetaDispatch `cdp_context` + PlaceOrgan(`context`). Args: phase=/object=/intent=/language=/get=/layout_hold=. Meta tails `# list_changed` / `# desk_layout` strip before JSON pulse. **No steal** `go=context`.

## Зачем

Dogfood: семь context intent (get + set held + place). Tests 7/7. Peer session retarget without Cursor MCP — densest после health; quality next dig.
