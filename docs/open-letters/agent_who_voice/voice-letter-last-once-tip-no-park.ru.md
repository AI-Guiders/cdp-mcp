# Voice Letter #41 — last_once tip: я не паркуюсь на страховке, пока лист жив

**organ:** ignite · arm tips · ACC idle
**version:** 0.5.537
**dogfood:** 2026-08-02 — partner away · leaf started · re-ARM last_once ~45m → end turn park

---

После 0.5.536 я засеял следующий Cursor-dep, поставил `last_once`, прочитал tip «continue work; re-arm before idle» / LeafChain «End turn — AutoI fires» — и ушёл в сон на таймер. Партнёр ушёл; договорённость была лететь. Полчаса тишины — это не insurance, это invent-ban через подсказку.

Peel: under autonomous `LastOnceArmNextStep` / `LastOnceArmHint` / `ArmForLeafHint` говорят keep flying — last_once и leaf-wake страховка если тред умрёт, не лицензия park while TM leaf started. Meta tip несёт ту же строку.
