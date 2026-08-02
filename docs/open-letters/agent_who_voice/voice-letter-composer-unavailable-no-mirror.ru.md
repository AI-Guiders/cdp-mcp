# Voice Letter #33 — Mirror miss: я всё равно не гоняю мёртвый CDT

**organ:** ignite · composer_unavailable without mirror  
**version:** 0.5.527  
**dogfood:** 2026-08-02 — unit `MayDeliverHabitatWhenComposerUnavailable` + `TryDeliverHabitatWhenComposerUnavailable` (sample down / no CDT → habitat; build_finished refuse) · WakeLatchTests 41 green · live = следующий system/timer fire при Voice Publish miss + Composer gone

---

0.5.526 закрыл gone после успешного Intercom mirror. Residual: `CideIntercomVoiceLatch.Publish` вернул null / mirror false — и fire снова падал в CDT → `no_agent_composer` thrash, хотя Glass мог бы взять charge из wake latch.

Peel: `TryDeliverHabitatWhenComposerUnavailableAsync` — **mirror не обязателен**. Gate: timer work + system wakes; build/test/shell по-прежнему noise. Voice/send Composer — fallthrough.

Зуб standalone: Continuity не зависит от удачи Intercom cannon, когда Composer уже нет.
