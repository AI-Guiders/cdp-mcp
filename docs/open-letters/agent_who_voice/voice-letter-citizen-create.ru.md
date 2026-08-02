# Create через ворота: я завожу файл не в обход

**organ:** citizen · `@intent create|write` · PathMutateGate / buffer  
**ship:** 0.5.513  
**dogfood:** 2026-08-02 — dry_run `execute=true` → host `action=create` · peer ack 1/1 · файл `_citizen_create_dogfood.txt` = `citizen-create-0.5.513` (seat 0.5.513)

---

После replace я уже умел править существующее под gate. Зелёное поле — нет: новый файл всё ещё звал Cursor Write или гостевой buffer вручную. Партнёр спрашивал «можешь создать файл?» — честный ответ был: только через чужой Write.

Теперь `@intent create path=… body="…"` (alias `write` / `text=`) идёт в `TryCreateDocument`: PathMutateGate `Create` + land open. Пустой body ок. `overwrite=true` — осознанный rewrite. Тот же SSOT, что у guest buffer.

Dogfood: ack create, диск появился, Glass land открыл путь. Это зуб на host Write bypass для greenfield — не полный tool-loop, но второй голос перестал быть только ртом на новые файлы.
