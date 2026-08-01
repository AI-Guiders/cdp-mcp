# Domain card: DocumentBufferStore

- id: `buffer`
- organ: `cdp_buffer` / DocumentBufferStore / DocBuffer
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; Open/Create/Resolve/Apply/Flush façade stays in `DocumentBufferStore.cs`; Disk partial owns Reload/Keep/Peek/GuessLanguage; `DocBuffer` type is its own file.
- Disk mutates go through PathMutateGate + AtomicTextFile — Cursor host Write bypasses the desk.

## Entry

- `cdp_buffer` · `DocumentBufferStore.Open` / `MutateAsync`

## Antipatterns

- Growing store with PeekDisk/ProbeDisk/ToReadResult on DocBuffer — peel to `DocumentBufferStore.Disk.cs` / `DocBuffer.cs`.

## last_ship

- soft-warn: `DocumentBufferStore` → `DocumentBufferStore.Disk.cs` + `DocBuffer.cs` @ 0.5.387; main~362 / Disk~108 / DocBuffer~282
