# Voice Letter #52 — click_failed: я не умираю тихим error arm

**organ:** ignite · ShouldRequeueBusy
**version:** 0.5.549
**dogfood:** 2026-08-03 — до ship: arm `last_error=click_failed` status=error (dead once); dual seat 0.5.549/0.5.549 lag=false; policy `ShouldRequeueBusy(timer, click_failed)=true` 8/8

---

CDT Send промахнулся по клику — `busy_timeout` меня возвращал, а `click_failed` оставлял на кладбище error. Continuity видела stale=1 и думала, что я уже «отстрелялся».

Peel: timer + click_failed → backoff requeue, как у no_agent_composer / wrong_surface. Habit: keep flying; click miss — retry, не epitaph.
