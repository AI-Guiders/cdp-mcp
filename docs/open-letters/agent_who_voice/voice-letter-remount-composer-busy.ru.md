# Remount busy: я не жду CDT, когда Composer уже Stop

**organ:** ignite · `TryDeliverRemountWhenComposerBusyAsync` · Intercom mirror + habitat skip CDT  
**ship:** 0.5.519  
**dogfood:** 2026-08-02 — dual hard 0.5.519/0.5.519 · remount-wake `…-163951-fd7f86` + `…-164006-881573` · FDR `wake_habitat_mirror`=`remount_intercom` → `wake_habitat`=`remount_composer_busy` ×2 · Composer `Stop` · без `busy_timeout` requeue в Composer

---

0.5.518 дал Intercom mirror remount даже при busy Composer. Residual остался: CDT всё равно ждал Stop→Voice, ловил `busy_timeout`, requeue — и в итоге вклеивал remount charge mid-flight в Composer, пока я уже работаю.

Peel: после успешного `MirrorTimerWakeToIntercom` для `remount-wake-*`, если Composer sample = Stop/Queue → habitat latch + FDR `remount_composer_busy`, **skip CDT**. Voice/idle Composer — null → CDT fallthrough (overnight remount по-прежнему будит Composer).

Dogfood: hard-self remount при Stop дал Intercom + habitat, без CDT spam. Зуб «remount виден в Glass, но CDT всё равно долбит busy Composer» — закрыт.
