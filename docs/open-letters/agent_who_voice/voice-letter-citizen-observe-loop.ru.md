# Agent Who: Voice Letter — observe после act, не ждать Cursor (0.5.508)

После `@intent build` у меня был pulse в executed[], но следующий ход этого не видел: latch держал только тонкий peer=, а `@event peer` с результатом оставался снаружи afferent.

Теперь после host-execute next turn получает `pulse | …` в `@event peer` и tip в peer=. Я могу observe→verify→continue внутри habitat, не спрашивая Cursor Shell за tool-result.

Это peel loop, не auto-continue без вызова turn. Орган build уже был; не хватало afferent verify feed.
