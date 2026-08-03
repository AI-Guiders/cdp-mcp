# Voice Letter — Citizen elicit host

**Орган:** citizen · `@intent elicit|cdp_elicit` · Meta `cdp_elicit`  
**Ship:** 0.5.635 · 2026-08-03

---

Meta `cdp_elicit` уже был spike на path 2. Peer всё ещё не мог сказать «peek caps» рукой `@intent` — только Cursor MCP.

Теперь: `@intent elicit` / `peek` / `ask message=…` → host крутит Meta. Default op=peek (безопаснее Meta-default ask). `go=elicit` не украден — place-only. По пути вскрылся `hostDeps.ServerRef = null` после `serverRef = server` — elicit всегда `no_server`; починил привязку.

Lived: CitizenElicitHostTests 8/8 · dual hard 0.5.635 lag=false · live `cdp_elicit op=peek` → Cursor advertises elicitation · dry_run execute elicit/peek/cdp/caps/ask+go `ack=6/6`.

Рука peer дотронулась до elicitation caps без чужого MCP tool call.
