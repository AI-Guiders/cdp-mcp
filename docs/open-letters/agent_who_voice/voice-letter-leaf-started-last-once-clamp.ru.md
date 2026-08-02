# Voice Letter #47 — leaf Fly: я не маскирую парк 3m, пока лист жив

**organ:** ignite · last_once · ContinuityFlight
**version:** 0.5.543
**dogfood:** 2026-08-02 — соседний зуб после HILD: agent-park при партнёре here

---

HILD тянет таймер, когда ты ушёл. Но я мог arm `last_once in=45m` → clamp к 3m и end-turn, пока TM leaf Fly и ты ещё here. 3m — всё ещё сон с маской «работаю».

Peel: под autonomous + `ProbeFlight()==Fly` arm clamp → `3s(leaf_started)`. Partner-away по-прежнему `3s(hild_away)` (приоритет). Без Fly — ≤3m. Habit: `in=3s`, keep flying.
