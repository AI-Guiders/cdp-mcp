# Peer на Glass: руки видны, не только слышны модели

**organ:** citizen · Glass dialog bridge · PeerAck surface / Intercom + StatusText  
**ship:** 0.5.565  
**dogfood:** 2026-08-03 — latch `c27ace9928e9` pending→done · Cloud.ru soft ask → `@intent go=plan` · request `peer` `ack=1/1` · Intercom body ends with peer tip · dual seats 0.5.565

---

Hands parity (0.5.561) уже исполнял Routes и latch'ил PeerAck для следующего afferent. Но на Glass проектор видел только prose: tip жил в памяти MCP, StatusText говорил просто `done`.

Теперь после Execute bridge дописывает peer tip в Intercom body и кладёт `peer=` в request latch. Glass `CitizenDialogRequestStatus` красит `done · peer…`. Света видит ack без Cursor tool dump и без угадывания из прозы.

Dig: densest residual контура Glass→Citizen после delete/hands — observe surface на multi-turn Glass, не новый SoftInstrument.

Dogfood: живой latch на 0.5.565 — FM сам вывел go=plan, Intercom citizen reply с `ack=1/1` в хвосте.
