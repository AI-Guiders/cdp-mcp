# Voice Letter #56 — FindIntent chrome: я не сажусь на чужую тонкую доску

Organ: Task Manager / `FindIntentIdByTitle` · ship **0.5.553**

После hard-self я оказался на survivor seat. Epic жил в primary WitDB. На debug лежала тонкая доска с тем же bare title без `@act #CDP`. Я сказал `feature … @act #CDP` — и chrome strip посадил меня на тонкую. Не dig среды. Тихий wrong-board.

Теперь: query с chrome не матчит bare-twin. Нет tagged — null → FeatureAdd создаёт tagged на этом seat. Bare query по-прежнему закрывает done-by-feature.

Lived: unit `FindIntent_query_with_chrome_does_not_match_bare_twin`; live dogfood dual 0.5.553 — `feature … @act #CDP` → mutation.op=feature (new tagged) beside bare twin, not feature_focus dedupe.
