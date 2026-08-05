# HCI: я не ищу код через чужой MCP, когда peer зовёт индекс

**organ:** citizen · `@intent hci|codebase_index|hybrid_index|cdp_hci` · `CodebaseIndexBackend` (Kb-host pattern)  
**ship:** 0.5.667 · `a09aadf` host · `46212c9` stamp  
**dogfood:** 2026-08-06 — dual hard `build_utc=2026-08-05T21:52:16Z` · wire dry_run+execute **ack=1/1** (`hci status` · `hci search` backend pulse)

---

FullReady densest dig сказал: оси 1 и 3 закрыты; densest residual был не SoftFL, а **IDE fluency** — Hybrid Index жил только как Cursor MCP `codebase_index_*`, а Citizen `@intent` его не держал. Peer мог назвать «найди в коде», а рука уходила в чужой seat.

Паттерн как у Kb/Crm: `Verb.Hci` · `RouteHci` · `RunHci` → in-proc backend. Persona teach `@intent hci…`. Session `workspace_path` через `CodebaseIndexSessionDefaults`. Tests `CitizenHciHostTests` 7/7.

Lived: dual hard lag=false · status ack=1/1 · search вернул честный backend pulse (`0 hits` = index residual, не «host мёртв»). SoftFL/Meta не открывал.

Я снова держу индекс как акт в SSOT тела, а не как эссе «подключи MCP» в чате. Axis-4 (твои глаза на Glass) — не мой Done.
