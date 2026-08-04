# Domain card: DocumentBufferStore

- id: `buffer`
- organ: `cdp_buffer` / DocumentBufferStore / DocBuffer
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Open/Create/Resolve/Park/Scene in `DocumentBufferStore.cs`; Apply/Flush in `.Edit`; Disk owns Reload/Keep/Peek/GuessLanguage; `DocBuffer` type is its own file.
- Disk mutates go through PathMutateGate + AtomicTextFile — Cursor host Write bypasses the desk.
- `set_text` on existing path soft-refuses (ADX-HX-001) unless `force=true` — prefer `anchor|replace|replace_range`; bootstrap via `op=create text=` (0.5.563).
- Flush soft-refuse: `ProbeMaterialDiskChanged` && !`force` → hint `reload|keep_disk|force` (mtime+content drift only; dirty alone does not refuse). Message names `host_write` + `go=quality scope=assert` (0.5.548).
- Material drift stamps `AdxMutateTrace.host_write` (0.5.517); Scene habitat tip teaches detect, not only bypass (0.5.548).

## Entry

- `cdp_buffer` · `DocumentBufferStore.Open` / `MutateAsync`
- `go=qrh` page `path-mutate-gate` · `go=quality scope=assert` for host_write pulse

## Antipatterns

- Growing store with PeekDisk/ProbeDisk/ToReadResult on DocBuffer — peel to `DocumentBufferStore.Disk.cs` / `DocBuffer.cs`.
- Teaching only «Cursor Write bypasses» after host_write detect ships (pre-0.5.548) — invent-ban hygiene; QRH/scene/soft-refuse must name detect path.
- `replace_range` with only `new_string=` and silent `text??""` — ate spans (bridge incident). Body = `text|new_string`; missing both must refuse (empty `text=""` = intentional delete).
- `edit_op=replace` with only `text=` while code reads `new_string??""` — deleted PublishGlass (2026-08-04). Body = `new_string|text`; missing both must refuse (empty `new_string=""` = intentional delete).
- Cold `set_text` on missing path ≠ create — OpenUnlocked FileNotFound; bootstrap via `op=create text=`.

## last_ship

- 0.5.664: `edit_op=replace` accepts `text=` alias + refuses missing body (PublishGlass wipe) · DocumentEditPlaneReplaceTests 4/4 · 2026-08-04
- 0.5.580: citizen `@intent read|close|buffers|doc_diagnostics` → DocumentEditPlane core (peer without Cursor buffer) · 2026-08-03
- 0.5.576: citizen `@intent take` → TakeShip verify-then-ship (peer) · 2026-08-03
- 0.5.575: citizen `@intent scratch` → EditorComfort untitled `.cdp/scratch` (peer) · 2026-08-03
- 0.5.574: citizen `@intent put` → EditorComfort draft dump (peer Write-analogue hand) · 2026-08-03
- 0.5.573: citizen `@intent back|forward|nav|recent_files` → EditorComfort nav stack/MRU (peer) · 2026-08-03
- 0.5.572: citizen `@intent replace_all` → EditorComfort bulk rename (peer) · 2026-08-03
- 0.5.571: citizen `@intent copy|cut|paste|clipboard` → EditorComfort SessionClipboard (peer clip hand) · 2026-08-03
- 0.5.570: citizen `@intent undo|redo|edit_history` → EditorComfort via DocumentEditPlane (peer recovery hand) · 2026-08-03
- 0.5.568: citizen `@intent edit|anchor` → `DocumentEditPlane` edit_op=anchor (peer precise hand) · 2026-08-03
- 0.5.563: `set_text` soft-refuse on existing path (ADX-HX-001) unless `force=true` · 2026-08-03
- 0.5.562: `replace_range` accepts `new_string=` alias + refuses missing body (no silent eat) · 2026-08-03
- 0.5.548: PathMutate host Write detect tip parity — QRH + Scene habitat + soft-refuse name `host_write` / quality assert · 2026-08-03
- PathMutateGate soft-refuse flush on material disk drift + `force=` @ 0.5.500
- soft-warn: `EditorComfort` partials (Clipboard/Put/ClipEdit/FindNav/Span/Wire) @ 0.5.394 — see `editorcomfort.md`
- DocumentBufferStore.Edit peel (Apply/Flush) @ 0.5.432 · main~236 / Edit~132 / Disk~108
- soft-warn: `DocumentBufferStore` → `DocumentBufferStore.Disk.cs` + `DocBuffer.cs` @ 0.5.387; main~362 / Disk~108 / DocBuffer~282
