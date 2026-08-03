# @intent analysis: я сам открываю analysis_scene, не через чужой cdp_analysis_scene MCP

**organ:** citizen · `@intent analysis|analysis_desk|analysis_scene|cdp_analysis_scene|analysis_clones|analysis_correspondence|analysis_semantic*|cdp_analysis_*` · Meta `cdp_analysis_scene` (`go=analysis_scene`)
**ship:** 0.5.609
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → analysis/desk/scene/cdp/clones/correspondence · `ack=6/6` · dual 0.5.609 lag=false
**tests:** CitizenAnalysisHostTests 6/6

## Было

`cdp_analysis_scene` Meta уже жил (map / correspondence / semantic_map / clones). DeskGoMap `analysis` → Meta. Peer без Cursor мог только place — реальный feature оставался за чужим CallTool.

## Стало

`@intent analysis|analysis_scene|cdp_analysis_*|analysis_clones|…` → `RunAnalysis` → MetaDispatch `cdp_analysis_scene` + PlaceOrgan(`analysis_scene`). Bare/desk/scene/cdp → feature=map; compounds inject feature; no steal bare clones|related|map|correspondence (IDE related stays).

## Зачем

Dogfood: шесть intent — map×4 / clones file / correspondence. Tests 6/6. Peer code-analysis desk без Cursor MCP — densest после edit_plan; elicit Cursor-spike пропущен намеренно.
