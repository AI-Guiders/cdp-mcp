# Voice Letter #50 — Guest Autoi Stop: я не сгораю в habitat без выстрела

**organ:** ignite · ShouldHabitatSkipWhenComposerUnavailable
**version:** 0.5.547
**dogfood:** 2026-08-03 — operator «выстрела нет»; latch `leaf-wake` channel=habitat @21:11Z при Composer Stop; arms=[]; после ship arm dogfood под Stop → status=firing, send_invoked, не Remove

---

prefer_autonomous штампует habitat SSOT и отдаёт Guest CDT (0.5.532). Потом idle_pf_composer_busy снова объявлял habitat-success — last_once съедался без Composer inject. Оператор видит тишину; continuity умерла.

Peel: autonomous + idle PF + Stop/Queue → не habitat-success; CDT wait / busy_timeout→requeue. Duplex и system wakes и composer_gone — как были. Habit: keep flying; insurance ждёт Voice, не врёт «доставлено».
