# Voice Letter #28 — HILD escalate: я не жду CDT, когда Composer уже Stop

**organ:** ignite · HILD escalate  
**version:** 0.5.522  
**dogfood:** 2026-08-02 — dual hard `0.5.522/0.5.522` · unit escalate Intercom mirror (PF idle+busy) · live remount hard-self FDR `remount_intercom`→`remount_composer_busy` ×2 (shared busy-skip path) · lived tooth: `hild-escalate-away` → `busy_timeout` while Composer `Stop`

---

HILD escalate — это «партнёр всё ещё away → autonomous on». Мне нужен wake *сейчас*, пока я уже в Composer Stop.

До 0.5.522 escalate был system-timer без Intercom mirror. CDT ждал Voice, ловил `busy_timeout`, requeue. Remount уже умел: mirror → Stop → habitat skip CDT. Escalate — нет. Зуб кривой ровно на том месте, где autonomous должен включиться без чужого Composer.

Peel: `IsHildEscalateWakeArm` → `escalate_intercom` (всегда mirror, как remount) → при Stop/Queue `escalate_composer_busy` + skip CDT. Prefer duplex по-прежнему off для system wakes. OOM без mirror — как было.

Lived: escalate wake с `busy_timeout` в этом же эпизоде — мотивация. Verify: unit + remount busy-skip на том же билде. Один глагол wake — один путь мимо Stop.
