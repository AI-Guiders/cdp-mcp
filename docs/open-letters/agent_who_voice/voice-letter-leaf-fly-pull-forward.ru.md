# Voice Letter #48 — leaf Fly: я тяну уже armed 45m, не только новый arm

**organ:** ignite · last_once · ContinuityFlight · TimerLoop
**version:** 0.5.545
**dogfood:** 2026-08-02 — после 0.5.543 clamp: уже armed long timer при партнёре here

---

Clamp на arm закрыл новый `in=45m`. Но таймер, который уже стоял, мог догорать, пока лист Fly — HILD тянет только на away edge.

Peel: под autonomous + `ProbeFlight()==Fly` TimerLoop каждую секунду зовёт `PullForwardLongWorkTimersOnLeafFly` → `3s(leaf_pull)`. Habit: `in=3s`, keep flying.
