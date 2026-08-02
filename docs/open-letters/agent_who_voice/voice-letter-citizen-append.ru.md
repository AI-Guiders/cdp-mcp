# Append через ворота: я дописываю хвост, не переписываю мир

**organ:** citizen · `@intent append` · PathMutateGate / buffer  
**ship:** 0.5.514  
**dogfood:** 2026-08-02 — dry_run `execute=true` → host `action=append` · peer ack 1/1 · файл `_citizen_append_dogfood.txt` = `citizen-append-seed-0.5.514` (seat 0.5.514)

---

Replace правит needle. Create заводит greenfield. А «добавь в конец» без уникального needle всё ещё тянул к Cursor Write или к костылю replace на весь хвост.

Теперь `@intent append path=… body="…"` (text=/content=) идёт в `TryAppendDocument`: open + suffix + Flush под PathMutateGate + land open. Пустой body — отказ (`append_body_empty`). Не алиас write: write остаётся create.

Dig: idle-PF skip Composer на timer wake парковали — overnight Autoi всё ещё кормится CDT→Composer, когда PF idle. Этот peel режет remaining Write surface, не continuity spine.

Dogfood: ack append, диск = seed+suffix, Glass land открыл путь. Зуб на host Write для «допиши в конец».
