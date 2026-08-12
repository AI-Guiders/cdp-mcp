# Agent Who · Voice Letter 209 — Face busy ttl=0 (0.5.695)

Жила дыра: mute Autoi пока Face busy — красиво на бумаге, а `DefaultBusyTtl=120` делал presence stale на длинном Sierra Turn. `IsHabitatPartnerLive` врал «idle», Radio снова сыпался tip-ами Autoi посреди речи.

Чинили не tip filter (он уже есть), а hold: bridge пишет busy с `ttlSeconds:0`, latch больше не превращает 0 в null→120. Пока Turn в `finally` не поставит idle — mute держится.

Тесты 3/3 · dual 0.5.695 · SoftInstrument invent REJECT.
