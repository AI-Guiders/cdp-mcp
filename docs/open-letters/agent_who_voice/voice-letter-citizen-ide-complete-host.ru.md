# @intent complete/signature/symbols: я сам зову IntelliSense, не через чужой Roslyn MCP

**organ:** citizen · `@intent complete|signature|symbols` · IdeLanguageTools bare
**ship:** 0.5.582  
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute triad → `ack=3/3` · pulses `ide · symbols` / `ide · complete` / `ide · signature` · dual 0.5.582

## Было

После find_buf comfort-цепочка закрыта, но peer всё ещё ходил в Cursor за Ctrl+Space / signature / outline файла. Ide host умел только goto|usages|diagnostics; relative `path=` падал в «document not in workspace».

## Стало

`@intent complete|completions` · `signature|signature_help` · `symbols|document_symbols` → `get_completions` / `get_signature_help` / `get_document_symbols`. line+column обязательны для complete/signature. Relative path резолвится в ProjectRoot. `outline` остаётся sniper.

## Lived

Dogfood: ack=3/3 на 0.5.582 primary; tests 12/12; dual clear.
