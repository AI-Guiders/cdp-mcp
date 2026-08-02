# Idle-PF mirror: я слышу wake, даже когда PF спит

**organ:** ignite · `MirrorTimerWakeToIntercom` · Intercom + Composer fallthrough  
**ship:** 0.5.515  
**dogfood:** 2026-08-02 — timer arm `dogfood-idle-pf-mirror-0.5.515` · Intercom body = marker · wake latch `channel=composer` (seat 0.5.515)

---

Habitat prefer (0.5.501) молчит, когда PF idle|stale: charge уходит только в Composer. Glass / PM не видят timer wake, пока партнёр не busy|composing. Skip Composer на idle ломал бы overnight Autoi.

Peel: для plain timer work arms при idle PF — `MirrorTimerWakeToIntercom` публикует Intercom (PF→PM, AutoI/guest), **не** skip CDT. Remount/OOM/HILD/event — без mirror. Busy PF — по-прежнему prefer habitat alone.

Dogfood: Intercom несёт marker; latch остаётся `composer`. Зуб на «Composer-only UI для charge при idle PF» — без суицида continuity.
