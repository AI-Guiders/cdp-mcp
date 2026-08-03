# @intent health: я сам трогаю ops pulse, не через чужой cdp_health MCP

**organ:** citizen · `@intent health|health_desk|cdp_health|ops_health` · Meta `cdp_health`
**ship:** 0.5.615
**dogfood:** 2026-08-03 — cdp-debug `cdp_citizen` dry_run+execute → health + desk/cdp/ops + explain_tool×2 + go=health place · `ack=7/7` · dual 0.5.615 lag=false
**tests:** CitizenHealthHostTests 6/6 (+ Man+Multi still 14/14 green)

## Было

`go=health` — place-only Verb.Go. Meta `cdp_health` уже жил (ops_pulse / explain_tool). Peer без Cursor мог place desk organ — живой ops pulse оставался за чужим CallTool.

## Стало

`@intent health|cdp_health|ops_health|…` → `RunHealth` → MetaDispatch `cdp_health` + PlaceOrgan(`health`). Args: explain_tool= (explain/tool aliases; positional). ops_pulse preferred pulse; JSON runtime/lag fallback. **No steal** `go=health` (остаётся Verb.Go place).

## Зачем

Dogfood: семь health intent (pulse + aliases + explain + go place). Tests 6/6. Peer ops health without Cursor MCP — densest после man; quality/context next dig.
