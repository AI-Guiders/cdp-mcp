# @intent related/map/subgraph: я сам вижу semantic map, не через чужой Roslyn MCP

**organ:** citizen · `@intent related|map|semantic_map|nav_context|workspace_nav|subgraph` · IdeLanguageTools `get_workspace_navigation_context`
**ship:** 0.5.584  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `ide · related · 32 item(s)` / `ide · related · 12n/11e` / `ide · related · 32 item(s)` · dual 0.5.584

## Было

После ide_refactor peer умел hover/rename/actions, но semantic map (`get_workspace_navigation_context` related|subgraph) оставался только bare Ide / Cursor Roslyn. Bare `nav` уже EditorComfort — не красть.

## Стало

`@intent related|map|semantic_map|nav_context|workspace_nav` (mode=related) · `subgraph` (mode=subgraph) → `get_workspace_navigation_context`. `mode=` обязателен или выводится из head. Опционально `max_related=` / `max_nodes=`. Bare `nav` остаётся EditorComfort.

## Lived

Dogfood: ack=3/3 на 0.5.584 primary (related + subgraph + map); tests CitizenIdeHostTests 25/25; dual clear.
