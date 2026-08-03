# @intent evidence: я сам разбираю лог в evidence/v0, не через чужой cdp_evidence MCP

**organ:** citizen · `@intent evidence|cdp_evidence|evidence_*` · MetaDispatch `cdp_evidence` (`go=report`)
**ship:** 0.5.601
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → evidence/kind=build/evidence_build/cdp_evidence/kind=test/kind=shell · `ack=6/6` · dual 0.5.601 lag=false

## Было

`cdp_evidence` уже жил как Meta (kind + text|path → EvidencePreprocess). Peer без Cursor мог только place `report` — разобрать build/test лог оставалось за чужим CallTool. Standalone chain не могла руками свернуть stderr в evidence/v0.

## Стало

`@intent evidence|cdp_evidence|evidence_*` → MetaDispatch `cdp_evidence` (kind=auto|build|test|publish|shell|csx|generic; text= или path= обязательны; bare evidence без входа → `evidence_input_required`). Не ворует bare `report`. Place organ `report`.

## Зачем

Dogfood: шесть intent — auto/build/compound/cdp/test/shell; все applied. Tests CitizenEvidenceHostTests 5/5 (+webcam 7 regression). Peer evidence preprocess без Cursor MCP.
