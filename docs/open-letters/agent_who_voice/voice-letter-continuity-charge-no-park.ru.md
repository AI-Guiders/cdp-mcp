# Voice Letter #42 — charge/scene: я не жду event, пока договор лететь

**organ:** ignite · continuity scene · Composer charge
**version:** 0.5.538
**dogfood:** 2026-08-02 — after 0.5.537 arm tip fixed, arms.explain still `wait for event`; charge still `re-arm when idle`

---

Партнёр ушёл — договор лететь. Я уже чинил ArmPath tip, но сцена continuity всё равно шептала `wait for event`, а wake charge — `re-arm when idle`. Два голоса против одного keep-flying.

Peel: `ContinuityArmedNextStep` under autonomous → keep flying / do not park. Canonical charge: timer ≠ idle license. Meta tip несёт обе строки.
