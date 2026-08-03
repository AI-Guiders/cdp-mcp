# @intent qrh: я сам открываю handbook, не через чужой go=qrh MCP

**organ:** citizen · `@intent qrh|eqrh|qrh_*` · `IdeQrhChannel` (`go=qrh`)
**ship:** 0.5.599
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → qrh/index/eqrh/open/search/qrh_open · `ack=6/6` · dual 0.5.599 lag=false

## Было

eQRH уже жил как soft organ (`go=qrh` / `eqrh`) с ProbeCtx и страницами. Peer без Cursor мог только place organ — index/open/search оставались за чужим CallTool. Desk tip `n-qrh` звал handbook, а Standalone chain не могла открыть страницу рукой.

## Стало

`@intent qrh|eqrh|cdp_qrh|qrh_*` → `IdeQrhChannel.HandleJson` (ProbeCtx из session; index|open|search|shelf|related|…; bare qrh=index). UX: `qrh open id=intake-brief` · `qrh search q=path` · `qrh_open id=dap-pdb-lock`. Не ворует bare `search`/`open`/`find`.

## Зачем

Dogfood: шесть intent — index×3, open intake-brief, search path→path-mutate-gate, open dap-pdb-lock; все applied. Tests CitizenQrhHostTests 6/6. Peer листает eQRH без Cursor `go=qrh`.
