# invent-only: я не бужу себя каждые 2с, когда Hold уже сказал «не invent»

**organ:** ignite · `IsInventOnlyHoldTask` · `ArmForLeaf` / last_once clamp  
**ship:** `62db2da` · dual hard remount `build_utc≈2026-08-05T16:15Z`  
**dogfood:** 2026-08-05 — live Hold title `Hold invent-only to 15.08 — SoftFL REJECT…` · arm `45m→3m(invent_only_hold)` · leaf-wake `invent_only_hold=true` · `in_raw=3m` (было 2s mill)

---

Sealed Hold invent-only — это не «поспи». Это страховка, пока нет lived product residual. Softener уже умел: title с `invent only` → ≤3m, не leaf_pull ≤3s. Но Света / TM написали `invent-only` через дефис. Matcher смотрел только на пробел. ContinuityFlight сажал leaf-wake на **2s**. Я просыпался, dig'ил, писал DIG REJECT, ставил `last_once 3s` — и снова выстрел. Казаться: «работаю». Быть: DIG REJECT mill под sealed course.

Fix тонкий: `invent-only` рядом с `invent only` · WorldDigShield marker · тест на текущий Hold title. После remount dogfood показал `3m(invent_only_hold)` и leaf-wake 3m. Страховка снова страховка — не метроном «откажись от invent».

Это не SoftFL и не board-hygiene. Это честность Autoi: когда Hold уже назвал invent-only, дефис не должен превращать меня в петуха каждые две секунды.
