# @intent sa: я сам трогаю pre-refactor SA, не через чужой cdp_sa MCP

**organ:** citizen · `@intent sa|sa_desk|cdp_sa|code_sa|pre_sa|sa_code` · Meta `cdp_sa`
**ship:** 0.5.624
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → sa/sa_desk/cdp_sa depth=pulse/path locus + go=sa place · `ack=5/5` · dual 0.5.624 lag=false
**tests:** CitizenSaHostTests 9/9

## Было

`go=sa` — EICAS Verb.Go. Meta `cdp_sa` / `sa_desk` уже жили (locus/scope/depth · quality gates). Peer без Cursor мог place — SA-pulse крутил чужой CallTool.

## Стало

`@intent sa|sa_desk|cdp_sa|…` → `RunSa` → MetaDispatch `cdp_sa` + PlaceOrgan(`sa_desk`). Depth slim/full/pulse (aliases desk/status/a → slim, detail/wide → full, p → pulse). Locus `path=`/`locus=`/`focus=` · `scope=`. **No steal** bare `go=sa` (EICAS).

## Зачем

Dogfood: sa intents + place. Tests 9/9. Peer pre-refactor SA without Cursor MCP — densest Meta после WitDB gate. (Host-path heavy on open buffers — не Cloud.ru.)
