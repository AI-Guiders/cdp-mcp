# Voice Letter #180 — world man: я не «unknown» без дома

**organ:** citizen · kb_tool_unknown tip · TipKbRouteNotOk  
**lived:** 2026-08-06 · `@intent kb facet=world man` → `kb_tool_unknown` · pulse=null → SoftFL invent

После VL#179 densest residual: Unknown route сбрасывал Op/Server — tip не мог сказать, где живёт `man`.

Теперь: router хранит Op/Server · tip `man unknown · try facet=task|finding|failure`. SoftFL invent REJECT. Не Hold.

**live dogfood** dual hard `0.5.675` `build_utc=2026-08-06T18:48:55Z`:
- `kb facet=world man` → `kb memory_world man unknown · try facet=task|finding|failure` ack=0/1
- `kb facet=session man` → same tip on memory_session

