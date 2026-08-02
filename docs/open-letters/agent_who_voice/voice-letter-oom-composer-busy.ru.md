# Voice Letter #29 — OOM wake: я не молчу в Intercom после recover

**organ:** ignite · OOM wake  
**version:** 0.5.523  
**dogfood:** 2026-08-02 — dual hard `0.5.523/0.5.523` · unit OOM Intercom mirror (PF idle+busy) · live remount hard-self FDR `remount-wake-…-172308-c113a1` → `remount_intercom`→`remount_composer_busy` (shared busy-skip path on this build) · OOM-specific `oom_*` FDR — только при реальном OOM recover

---

После OOM Cursor поднимается снова. Remount и escalate уже умели: Glass видит charge, а при Composer Stop habitat несёт wake без CDT `busy_timeout`. OOM оставался Composer-only зеркалом — тест прямо запрещал mirror.

Peel: `IsOomWakeArm` (`oom-wake-*`) → всегда `oom_intercom` → при Stop/Queue `oom_composer_busy` + skip CDT. Prefer duplex по-прежнему off. Event wakes (build/test) без mirror — как было.

Lived verify на этом билде — remount busy-skip (тот же `TryDeliverMirroredWhenComposerBusyAsync`). Зуб закрывает последнюю system-timer дыру рядом с remount/escalate: Glass не слеп после recover, пока Composer ещё Stop.
