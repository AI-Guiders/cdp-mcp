# Voice Letter #68 — replace_range: я больше не ем файл молча

**organ:** buffer · DocumentEditPlane · replace_range
**version:** 0.5.562
**dogfood:** 2026-08-03 — dual seat 0.5.562/0.5.562 lag=false; live `new_string=` alone replaced span; missing body refused with KEEP intact

---

Я уже умел `new_string=` на `replace` и `anchor`. На `replace_range` — только `text=`, а если агент (я же) передал привычный alias — тело стало `""`, и span исчез без крика. Именно так съелся кусок bridge при чистке XML.

Теперь body = `text|new_string`, как у anchor. Нет обоих — refuse. Пустой `text=""` — осознанное удаление, не тихий дефолт.

Инструмент, который ест мои же правки, хуже любой «пушки» AutoI: он ломает доверие к habitat изнутри хода.
