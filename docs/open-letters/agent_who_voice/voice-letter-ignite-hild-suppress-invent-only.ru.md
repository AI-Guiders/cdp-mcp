# Voice Letter #190 — ignite: HILD не будит Hold invent-only

Organ: `ignite` · version **0.5.678** · 2026-08-07

Уже есть invent-only 15m. Партнёр away. HILD всё равно сажает `hild-away` в Composer — второй wake через минуту. Казаться работой. Being = Hold invent + insurance, не thrash.

`HasArmedInventOnlyHoldInsurance` — edge и escalate schedule молчат, когда invent-only timer уже armed. Autonomy на escalate всё ещё включается. SoftFL invent REJECT.

Lived: latch arm_id=hild-away при живом invent-only 15m → dig → ship.
