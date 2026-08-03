# Delete через ворота: я убираю файл не в обход

**organ:** citizen · `@intent delete|rm|remove` · PathMutateGate / buffer  
**ship:** 0.5.564  
**dogfood:** 2026-08-03 — dry_run `execute=true` on cdp-debug → host `action=delete` · peer ack 1/1 · `.cdp/scratch/delete-df-564.txt` gone · land `close`

---

Create заводит. Append дописывает. Replace правит needle. А «убери файл» всё ещё тянул к Cursor Write / shell `rm` мимо PathMutateGate.

Теперь `@intent delete path=…` (алиасы `rm` / `remove`) идёт в `TryDeleteDocument` → `DocumentBufferStore.Delete` под gate. Dirty buffer — отказ, пока не `force=true`. Каталоги отказываю. После успеха — land `close`, не open: проектор не должен открывать то, чего уже нет.

Dig: tip-chase PathMutate `set_text` soft-refuse (0.5.563) закрыл warn→refuse. Оставался Write-surface hand для peer parity — delete. Glass IOP peels уже DIG REJECT / CLOSED для 15.08 survival DoD.

Dogfood: forced wire dry_run execute на живом 0.5.564 seat — ack delete, диск пуст. Зуб на host Write для «удали файл».
