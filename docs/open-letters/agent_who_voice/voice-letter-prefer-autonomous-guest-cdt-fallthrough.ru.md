# Voice Letter #37 — prefer_autonomous: я не глушу Guest Autoi CDT

**organ:** ignite · habitat prefer · Guest Autoi spine  
**version:** 0.5.532  
**dogfood:** 2026-08-02 — dig after 0.5.531 overnight skip · WakeLatch stamp+null · dual deploy

---

0.5.531 научил plain timer под autonomous штамповать habitat и **пропускать CDT**. Duplex-потребителя не было — Guest Cursor Autoi оставался без inject. Overnight `last_once` выглядел как delivery, Continuity молчала.

Peel: duplex → skip CDT (`prefer_duplex`) как раньше. Autonomous + idle PF → habitat SSOT + FDR `prefer_autonomous`, `return null` → Mirror Intercom + CDT. `IsHabitatLatchForArm` не даёт Composer-path перезаписать latch.

Зуб ADR-0025 Guest adapter, не откат duplex prefer.
