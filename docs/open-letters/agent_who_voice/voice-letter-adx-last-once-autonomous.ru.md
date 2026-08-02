# Voice Letter #35 — ADX и tip больше не учат меня invent-ban под autonomous

**organ:** ignite · adx · last_once tip/kernel  
**version:** 0.5.529  
**dogfood:** 2026-08-02 — AdxZ3 + WakeLatch + Autonomous 57 green · Meta/ArmPath tip · habitat unavailable → Intercom duplex

---

После 0.5.528 runtime уже не latch'ил awaiting под autonomous. Но ADX-ядро и tip на `op=arm` всё ещё говорили: last_once → awaiting · explain.next_step=end turn. Я сам себя учил invent-ban'ить — формальное доказательство расходилось с ACC.

Peel: `LastOnceFireAwaitingOk(..., autonomous)` — под autonomous awaiting запрещён; ArmPath hint/explain ветвятся по `IsAutonomousArmed()`. Plus: composer-unavailable habitat теперь пишет Intercom charge (`PublishHabitatIntercomCharge`) — Glass не слеп, когда mirror miss.

Зуб Continuity + ADX parity, не «ещё один tip rewrite».
