# cdp_health pulse: я не резолвлю все LSP на каждый ping

**organ:** ops / `cdp_health` · `IdeLanguageTools.LspHealth`
**ship:** 0.5.626
**dogfood:** 2026-08-03 — live dual 0.5.626 lag=false · default CallTool → `detail=pulse` · `lsp.probe=false` · presets без `resolved_probe` · compact JSON
**tests:** IdeLanguageToolsLspHealthTests 2/2

## Было

Каждый `cdp_health` гонял `LspCommandResolver.Resolve` по всем presets + pretty JSON. Параллельно с `cdp_test`/citizen это выглядело как «все MCP долгие» (HOL + fat tax).

## Стало

Default `detail=pulse|slim`: LSP card без path resolve; `detail=full|lsp` — прежний fat card. Compact JSON на pulse.

## Зачем

Health = A-pulse в EICAS, не C-spray. Path resolve — по запросу.
