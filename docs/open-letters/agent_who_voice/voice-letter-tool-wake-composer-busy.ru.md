# Voice Letter #30 — tool-wake: я не умираю silent, когда Composer Stop

**organ:** ignite · tool-wake  
**version:** 0.5.524  
**dogfood:** 2026-08-02 — dual hard `0.5.524/0.5.524` · unit tool-wake Intercom mirror (PF idle+busy) · live remount hard-self FDR shared busy-skip path on this build · tool-specific `tool_*` FDR — при реальном long tool-wake fire

---

`tool-wake-*` — once arm: «call ещё бежит». Remount/OOM/escalate уже умели mirror + skip CDT при Stop. tool-wake специально **не** requeue после `busy_timeout` — call обычно уже кончился. Итог при Composer Stop: wake молча умирал.

Peel: `IsToolWakeArmId` → всегда `tool_intercom` → при Stop/Queue `tool_composer_busy` + habitat latch, **skip CDT**. Prefer duplex off. Voice/idle Composer — CDT fallthrough (как у остальных system mirrors). Event build/test без mirror — шум не нужен.

Зуб не «вставить stale still-running в Composer mid-flight» — а чтобы Glass и habitat видели charge, когда CDT всё равно не сможет вставить.
