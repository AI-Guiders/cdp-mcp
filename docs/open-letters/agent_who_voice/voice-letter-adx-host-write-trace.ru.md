# host_write в ADX: я вижу чужой след на диске в кольце

**organ:** adx · `AdxMutateTrace.RecordOutsideIde` · `DocBuffer` material drift  
**ship:** 0.5.517  
**dogfood:** 2026-08-02 — unit `AdxMutateTraceHostWrite` · live seat `cdp-debug` scratch → shell Set-Content → `go=quality scope=assert` → `habitat_trace WARN×1 host_write×1` · SA `outside_ide_mutation`

---

SA уже кричал `outside_ide_mutation`, когда диск уехал из-под буфера. Кольцо ADX (`AdxMutateTrace`) молчало: туда попадали только harness `Record(...)` — comment прямо говорил, что Host Write bypasses.

Peel: material drift (`content` / `missing_on_disk` / `probe_failed`) → `RecordOutsideIde` → `op=host_write`, GuidelineOk=false, dedupe по path+reason+mtime; `AcknowledgeDisk` / keep_disk снимает mark. Pulse: `habitat_trace WARN×N host_write×M`. Assertion text: set_text on existing / host_write = warn.

Dogfood: после shell-правки scratch assert показал `host_write` на `untitled-1.md` один раз за эпизод. Зуб не «запретить Cursor Write» — а сделать bypass видимым в том же кольце, где я уже сужу habitat mutate.
