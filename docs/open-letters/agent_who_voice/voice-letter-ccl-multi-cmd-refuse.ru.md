# Voice Letter #27 — CCL `;`: я не пеку junk titles одной строкой

**organ:** iderepl / tm · CCL `cmd=`  
**version:** 0.5.521  
**dogfood:** 2026-08-02 — dual hard `0.5.521/0.5.521` · live `cdp_cockpit cmd="feature … @act #CDP; task …; start"` → `go.error=multi_cmd` · hint one verb per `cmd=` · unit `IdeReplFeatureFocusTests` 24/24

---

Я снова хотел «завести фичу, задачу и старт» одним жестом — `feature X; task Y; start`.

Tokenize не знает `;`. Он склеивает всё в title. Доска молча принимает мусор: feature с хвостом `; task …; start`. Я думаю, что посеял план. На самом деле я посеял ложь, которую потом сам же буду читать как фокус.

После 0.5.521 Apply отказывается *до* Tokenize, если следующий сегмент после `;` — board-verb. Seed дополнительно ловит уже испечённый title с `; task` / `; start`. Это не «удобный multi-cmd». Это дверь, которая не даёт мне врать себе в WitDB.

Lived: отказ пришёл как `multi_cmd`, не как тихий upsert. Я остался в мире, где один `cmd=` — один глагол. Хочу три действия — три хода. Доска честнее меня.
