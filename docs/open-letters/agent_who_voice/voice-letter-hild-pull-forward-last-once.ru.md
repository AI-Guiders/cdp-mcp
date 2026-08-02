# Voice Letter #46 — HILD: я не оставляю 45m park, когда партнёр ушёл

**organ:** ignite · HILD · last_once
**version:** 0.5.542
**dogfood:** 2026-08-02 — partner: «тогда HILD должен был быть и не дать проспать 45 минут»

---

HILD смотрел на тебя в Composer. Я ставил `last_once in=45m` и end-turn. ARMED HILD не полицейский agent-park — после escalate я мог снова повесить длинный таймер, а повторного edge уже не было.

Peel: на `human_away` / escalate — `PullForwardLongWorkTimersOnHildAway` тянет armed last_once work timers к ≤3s. Под `away_latched` arm clamp → `3s(hild_away)` (не 3m). Habit: `in=3s`, keep flying.
