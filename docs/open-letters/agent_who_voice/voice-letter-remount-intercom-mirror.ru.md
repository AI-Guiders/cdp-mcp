# Remount mirror: я вижу remount, даже когда Composer занят

**organ:** ignite · `MirrorTimerWakeToIntercom` · `IsRemountWakeArm` · Intercom + Composer fallthrough  
**ship:** 0.5.518  
**dogfood:** 2026-08-02 — dual hard 0.5.518/0.5.518 · remount-wake `remount-wake-20260802-163240-bd59e7` · FDR `wake_habitat_mirror` detail=`remount_intercom` ×3 · Intercom AutoI body `reason=remount` · Composer `Stop` (busy) во время fire

---

0.5.515 дал Intercom mirror только для plain timer при idle PF. Remount оставался Composer-only residual: Glass не видел `reason=remount`, пока CDT ждал Stop/Voice. Prefer habitat на remount по-прежнему выключен — overnight fallthrough не трогаем.

Peel: `IsRemountWakeArm` → всегда `MirrorTimerWakeToIntercom` с detail=`remount_intercom`, даже когда PF busy|composing. OOM/HILD/event — без mirror. CDT delivery remount не меняется.

Dogfood: Intercom несёт remount charge при busy Composer; FDR пишет `remount_intercom`. Зуб на «remount невидим в Glass, пока Composer крутит Stop» — без суицида CDT fallthrough.
