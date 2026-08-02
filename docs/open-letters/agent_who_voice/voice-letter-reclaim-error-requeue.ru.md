# Voice Letter #53 — error tombstone: я воскрешаю, а не хороню

**organ:** ignite · ReclaimOverdue · SweepNoise · TryReviveRequeueableErrorUnlocked
**version:** 0.5.550
**dogfood:** 2026-08-03 — tests ReclaimOverdue_requeues_error_when_click_failed + Hygiene_requeues_click_failed 2/2; pre-ship live tombstone arm-…-2010c3 last_error=click_failed; dual seat after hard-self

---

0.5.549 научил fire path requeue. Но кладбище уже было: status=error с click_failed сидело в continuity как stale=1, пока hygiene на старом коде просто выкидывала труп.

Peel: ReclaimOverdue + SweepNoise зовут один revive, когда ShouldRequeueBusy сказал бы «живи». fire_failed по-прежнему drop. Habit: tombstone requeueable — resurrect, не epitaph.
