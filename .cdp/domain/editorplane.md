# Domain card: EditorPlane

- id: `editorplane`
- organ: `cdp_editor_scene` / `cdp_edit_plan`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `EditorPlane` is `partial` by concern.
- Partials: Core (Dispatch/Scene/ScenePulse/SceneFull) · Plan (Plan/Draft/Validate) · Apply (Apply/ValidateStep/BuildEditArgs) · Parse (EditSlice/YAML+JSON/helpers).
- Scene defaults to desk-parity pulse; `detail=full` maps buffers.
- edit_plan: draft → validate → apply logical YAML/JSON slices.

## Entry

- `EditorPlane.DispatchAsync` / `IsEditorTool`

## Antipatterns

- Re-inlining SceneFull + Apply + parsers into one mega-file.
- Skipping validate before apply.

## last_ship

- soft-warn peel: Core257 Plan233 Apply310 Parse342 @ 0.5.398
