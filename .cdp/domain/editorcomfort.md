# Domain card: EditorComfort (buffer undo/clipboard/nav)

- id: `editorcomfort`
- organ: `EditorComfort` / `cdp_buffer` comfort ops
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `EditorComfort` is `partial` by verb slice.
- Partials: Core (Dispatch/stack) · Clipboard · Put · ClipEdit · FindNav · Span · Wire.
- Nested `EditStack` stays in Core; `TakeSpan` stays in Span.
- Comfort ops route via `IsComfortOp` / `Dispatch` — do not re-scatter into DocumentBufferStore.

## Entry

- `EditorComfort.Dispatch` · `RecordEdit` · comfort ops (undo/redo/copy/cut/paste/find/nav/scratch)

## Antipatterns

- Re-inlining giant comfort switch past soft-warn.
- Moving clipboard/nav into DocumentBufferStore Disk peels.

## last_ship

- 0.5.570: citizen host-execute undo/redo/history — same EditorComfort stack as buffer comfort · 2026-08-03
- soft-warn peel: Clipboard/Put/ClipEdit/FindNav/Span/Wire @ 0.5.394; FindNav~347 · Core~180
