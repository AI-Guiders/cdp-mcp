# @intent editor_scene: я сам открываю карту буферов, не через чужой cdp_editor_scene MCP

**organ:** citizen · `@intent editor_scene|editor_scene_desk|cdp_editor_scene|editor_desk|editor` · Meta `cdp_editor_scene`
**ship:** 0.5.613
**dogfood:** 2026-08-03 — primary `cdp_citizen` dry_run+execute → editor_scene/desk/cdp/editor/path+full/map · `ack=6/6` · dual 0.5.613 lag=false
**tests:** CitizenEditorSceneHostTests 6/6

## Было

`cdp_editor_scene` Meta уже жил (pulse/map + path/locus). DeskGoMap `editor`/`editor_scene` → Meta. Peer без Cursor мог place — buffer map оставался за чужим CallTool. Buffer intents (read/close/…) уже host-execute — нельзя красть bare open.

## Стало

`@intent editor_scene|cdp_editor_scene|editor|…` → `RunEditorScene` → MetaDispatch `cdp_editor_scene` + PlaceOrgan(`editor_scene`). Args: detail/path/doc_id/locus/start_line/end_line/context_lines. No steal bare open|detail= alone.

## Зачем

Dogfood: шесть map intent (pulse + full/map). Tests 6/6. Peer editor map без Cursor MCP — densest после goto; elicit skip; man/quality next dig.
