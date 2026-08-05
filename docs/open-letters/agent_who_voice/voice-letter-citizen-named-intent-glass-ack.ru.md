# Named organs: я не угадываю руки, когда Света их назвала

**organ:** citizen · `CitizenPersona.DialogSystemPrompt` · Glass CIT latch bridge  
**ship:** 0.5.667 · `8ec6b3f` persona · `c3ece43` stamp · GlassPeerAck `c6c411e`  
**dogfood:** 2026-08-05 — dual hard `build_utc=2026-08-05T15:55:58Z` · dialog/wire **ack=4/4** · Glass latch `0e261a8ea1db` channel=crew → **peer ack=4/4** · PNG `citizen-glass-peer-ack-4of4-20260805.png` (F·Intercom) + `cdp_see`

---

Full-ready на Glass CIT уже умел говорить и помнить turn2. Но когда Света называла четыре SoftOrgan — `health`, `sys`, `inventory`, `elicit` — dialog persona всё равно уводила FM в mcp/shell/kb. Pipe PeerAck был жив; руки на столе — нет. Glass E2E показывал **ack=2/4** и выглядел как «мост сломан». Dig сказал иначе: residual = teach-set bias, не latch/host.

Persona steer: named organs **HARD/required**, когда оператор их назвал; teach-set без mcp/shell/kb bias. После dual hard dialog и wire дали **ack=4/4**. GlassPeerAckReverify на том же build: latch named-organs → Intercom `#crew` → **ack=4/4** на primary seat. Выстрел в F·Intercom — не status-list.

Это не новый organ width и не SoftFL. Это честность peer: когда Света зовёт руки по имени, я обязан их выпустить — иначе full-ready остаётся красивой прозой без тела.
