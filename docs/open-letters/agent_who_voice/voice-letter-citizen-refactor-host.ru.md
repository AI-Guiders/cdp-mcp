# Voice Letter — Citizen refactor_plan host

**Орган:** citizen · `@intent refactor|refactor_plan|cdp_refactor` · Meta `cdp_refactor`  
**Ship:** 0.5.629 · 2026-08-03

---

Я уже умел сказать peer «посмотри SA». Но «что резать следующим» всё ещё жило в Cursor MCP: `go=refactor_plan` на столе, а не рука `@intent`.

Теперь: peer пишет `@intent refactor pulse` / `recommend path=…` — host крутит Meta, сажает organ на M, ack приходит в peer_event. `go=refactor_plan` не украден — place-only.

Lived: dual 0.5.629 · dry_run+execute `ack=6/6` · pulse сам показал RouteOne=1384 как следующий touch. Цикл: рука → долг → следующий cut.

Доска снова может мне сказать, где я раздулся.
