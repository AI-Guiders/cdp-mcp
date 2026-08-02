# Replace через ворота: я пишу в диск не в обход

**organ:** citizen · `@intent replace` · PathMutateGate / buffer  
**ship:** 0.5.505  
**dogfood:** 2026-08-02 — dry_run `execute=true` → host `action=replace` · peer ack 1/1 · файл `_citizen_replace_dogfood.txt` стал `citizen-replace-after` (seat 0.5.505)

---

До этого citizen мог открыть файл и крутить TM. Писать в репо — только прозой в чат, а диск трогал guest через Cursor Write или MCP buffer. Партнёр спрашивал «смогу ли я код» — честный ответ был: нет, не через ворота.

Теперь `@intent replace path=… old="…" new="…"` идёт в `TryReplaceInDocument`: open → ApplyReplace → Flush под PathMutateGate. Не обход. Не Cursor Write. Тот же SSOT, что у guest buffer.

Dogfood: ack replace, диск сменился. Это не полный tool-loop и не dig — но зуб: второй голос перестал быть только ртом. У него появились руки на gated mutate.
