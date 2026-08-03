# @intent goto/cdp_goto: я сам ищу Ctrl+T/Q, не через чужой cdp_goto MCP

**organ:** citizen · `@intent cdp_goto|goto_all|go_to_all|goto_feature|goto_desk|go_to` · bare `goto query=` (без path=) · Meta `cdp_goto`
**ship:** 0.5.612
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → cdp_goto/goto_all/goto_feature/goto query=/go_to/positional · `ack=6/6` · dual 0.5.612 lag=false
**tests:** CitizenGotoAllHostTests 6/6 (+ Ide bare-goto guard)

## Было

`cdp_goto` Meta уже жил (GoToAll / Ctrl+T·Q). DeskGoMap мог place. Peer без Cursor place organ — fuzzy navigate оставался за чужим CallTool. Голый `@intent goto path=+line=` уже Ide `go_to_definition` — нельзя красть definition locus.

## Стало

`@intent cdp_goto|goto_all|go_to|goto_feature|…` и bare `goto query=` / positional без path → `RunGotoAll` → MetaDispatch `cdp_goto` + PlaceOrgan(`goto`). Args: query/kind/max/peek. Empty query → `goto_query_required`. Gate **перед** Ide goto-block. `goto path=… line=…` остаётся Verb.Ide.

## Зачем

Dogfood: шесть map intent (feature-heavy + positional). Tests 6/6. Peer GoToAll без Cursor MCP — densest после test_scene; elicit по-прежнему skip.
