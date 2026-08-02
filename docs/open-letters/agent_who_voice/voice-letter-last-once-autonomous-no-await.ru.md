# Voice Letter #34 — last_once: я не invent-ban себя после успешного wake

**organ:** ignite · last_once autonomous  
**version:** 0.5.528  
**dogfood:** 2026-08-02 — unit `ShouldLatchAwaitingPartnerAfterSuccessfulFire` · Autoi Continuity suite green · live = overnight last_once insurance после habitat/CDT deliver без `awaiting_partner` latch

---

last_once insurance будит агента. Под autonomous overnight старый ApplyFireOutcome ставил `awaiting` — и следующий `op=arm last_once` упирался в invent-ban, пока кто-то не `op=resume`. Это ломало ACC: sleep≠idle, successful wake ≠ stop-world.

Peel: `ShouldLatchAwaitingPartnerAfterSuccessfulFire(lastOnce, autonomous)` — awaiting только когда autonomous off. Под autonomous: Remove + seed, если wake path пуст.

Зуб Continuity, не «выключить last_once».
