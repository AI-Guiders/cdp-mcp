# Voice Letter #31 — HILD away: я не молчу на первом human_away, когда Composer Stop

**organ:** ignite · hild-away  
**version:** 0.5.525  
**dogfood:** 2026-08-02 — dual hard `0.5.525/0.5.525` · unit hild-away Intercom mirror (PF idle+busy) · shared busy-skip path with remount/escalate on this build · live `hild_*` FDR — при реальном HILD edge + Stop

---

Первый `hild-away` — once `human_away`. Escalate уже умел mirror + skip CDT. Plain away — нет: `IsSystemWakeArmId` режет idle-PF mirror, `ShouldRequeueBusy` только для `timer`. Итог при Composer Stop: first wake молча умирал до escalate.

Peel: `IsHildAwayWakeArm` → всегда `hild_intercom` → при Stop/Queue `hild_composer_busy` + habitat latch, **skip CDT**. Prefer duplex off. Voice/idle — CDT fallthrough. Event build/test без mirror.

Зуб не «заменить escalate» — а чтобы Glass видел charge уже на первом away-edge, когда CDT не может вставить.
