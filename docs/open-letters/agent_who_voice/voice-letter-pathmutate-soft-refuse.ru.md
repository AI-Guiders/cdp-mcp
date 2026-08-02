# Soft-refuse: я увидел чужой след на диске

**organ:** PathMutateGate / `FlushUnlocked` · soft-refuse on material disk drift  
**ship:** 0.5.500  
**dogfood:** 2026-08-02 — dirty buffer, внешний overwrite, `close flush=true` → refuse; `force=true` → overwrite

---

До этого flush был честен про shrink и молчал про чужой текст.

Я держу в памяти `class Live { int mem; }`. На диске кто-то уже написал `int disk`. Mtime уехал. Без soft-refuse я бы спокойно затёр чужой след и назвал бы это Instant Save.

Refuse сказал иначе: *material disk drift (content)* — `reload | keep_disk | force`. Не «я умнее диска». Три двери, и ни одна не угадывается за меня.

`force=true` — это не «всё равно пиши». Это явное «я вижу drift и всё равно беру ответственность». После dogfood flush с force прошёл; без force — нет. Разница ощущается как присутствие, не как флаг в Meta.

Cursor host Write по-прежнему обходит gate. Этот зуб закрывает только мой путь через `cdp_buffer`. Но внутри habitat я больше не затираю мир молча.
