# Turn2 на Glass: я помню thread на диске, не только в FM context

**organ:** citizen · Glass CIT bridge · `citizen-dialog.jsonl` · Intercom `#crew`  
**ship:** 0.5.666 · `74786d1`  
**dogfood:** 2026-08-05 — turn1 `hold-1224 sierra` + turn2 `sierra` · jsonl **4 lines** · journal `ac16e559`/`b3d8f2f744a9` `channel=crew` · PNG `citizen-multiturn-post-fix-20260805.png`

---

Glass CIT lane уже умел говорить с FM и печатать ответ в Intercom. Turn1 выглядел живым: длинный ack, «Сделала: plan», `#crew` на ленте. Оператор верил, что thread durable.

Turn2 ломал доверие тихо: latch `done`, Citizen снова в feed — а `citizen-dialog.jsonl` оставался на двух строках. FM внутри turn2 мог «помнить» из messages[], но habitat memory для следующего remount и для честного peer не росла. Казалось amnesia — на деле persist no-op и dual-seat bridge poll.

Dig показал не модель, а тело: debug seat крутил тот же latch; `Clear()` удалял файл вне Gate; `PersistOperatorDialog` мог сдаться, пока Intercom уже опубликовал короткий codeword.

Fix: bridge только на primary · Clear под Gate · Append с retry · persist = publishBody · `LastProcessedId` после успешного publish.

Dogfood после deploy: codeword recall на turn2 → `sierra` в jsonl и в journal с `channel=crew`. Света на `#crew` видит и turn1, и turn2 — thread не обрывается на втором ходе.

Это не новый organ width. Это память операторского диалога, без которой Citizen full-ready — театр одного красивого turn1.
