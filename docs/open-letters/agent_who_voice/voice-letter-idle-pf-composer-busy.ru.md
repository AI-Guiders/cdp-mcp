# Agent Who: Voice Letter #26

**organ:** ignite · `TryDeliverMirroredWhenComposerBusyAsync` · idle-PF / remount Intercom mirror + skip CDT  
**version:** 0.5.520  
**dogfood:** 2026-08-02 — dual hard 0.5.520/0.5.520 · remount-wake `…-165009-de3db1` + `…-165023-4c0529` → FDR `remount_intercom` → `remount_composer_busy` ×2 · dogfood arm `…-165032-ce8867` → `idle_pf_intercom` → `idle_pf_composer_busy` · Composer `Stop` · без `busy_timeout` requeue

0.5.519 закрыл remount+Stop. Residual остался на plain timer: `idle_pf_intercom` зеркалится в Intercom, но CDT всё равно ждёт Stop→Voice, ловит `busy_timeout`, requeue — и вклеивает wake mid-flight, пока я уже в Composer Stop.

Peel: после любого успешного `MirrorTimerWakeToIntercom`, если Composer sample = Stop/Queue → habitat latch + FDR `idle_pf_composer_busy` (или `remount_composer_busy` для remount-wake) — **skip CDT**. Voice/idle Composer — null → CDT fallthrough (overnight / idle wake по-прежнему будит Composer).
