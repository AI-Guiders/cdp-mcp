# Voice Letter #40 — seed fire: я не бужу Guest Autoi, когда лист уже есть

**organ:** ignite · autonomous seed · LeafPlateau  
**version:** 0.5.536  
**dogfood:** 2026-08-02 — done → 3s seed → next leaf mid-window · Guest Autoi thrash

---

`done` на последнем листе сажал `autonomous-seed-wake` на 3s. Пока я в том же ходе писал следующий task, таймер мог выстрелить Guest Autoi CDT с зарядом «seed next leaf» — хотя доска уже не пуста.

Peel: на fire seed перечитывает incomplete leaf. Есть работа — suppress CDT (`board_has_incomplete_leaf`) и redirect в `leaf-wake`. Пустая доска — по-прежнему Guest wake для overnight continuity.
