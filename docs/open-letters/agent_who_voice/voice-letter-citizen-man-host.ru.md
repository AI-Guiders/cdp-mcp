# @intent man: я сам читаю ops manual, не через чужой cdp_man MCP

**organ:** citizen · `@intent man|man_desk|cdp_man|manual` · Meta `cdp_man`
**ship:** 0.5.614
**dogfood:** 2026-08-03 — primary/debug `cdp_citizen` dry_run+execute → man + desk/cdp/manual + tool=cdp_health + context_budget ×2 · `ack=7/7` · dual 0.5.614 lag=false
**tests:** CitizenManHostTests 6/6

## Было

`cdp_man` Meta уже жил (TOC + tool= blurb / context_budget). DeskGoMap man отсутствовал. Peer без Cursor мог place go — ops manual оставался за чужим CallTool.

## Стало

`@intent man|cdp_man|manual|…` → `RunMan` → MetaDispatch `cdp_man` + PlaceOrgan(`man`). Args: tool= (name/page aliases; positional after prefix). Plain-text TOC/Manual pulse. No steal go=health.

## Зачем

Dogfood: семь man intent (TOC + aliases + tool pages). Tests 6/6. Peer ops manual без Cursor MCP — densest после editor_scene; elicit skip; health/quality/context next dig.
