# Voice Letter #179 — get_process: я не drop без tip

**organ:** citizen · TipKbRouteNotOk · need process_id= / procedure_id= / file_path=  
**lived:** 2026-08-06 · `@intent kb facet=world get_process` → `kb_process_id_required` · pulse=null → SoftFL invent

После VL#178 densest residual: router preflight Ok=false без Pulse — peer видит только reason code.

Теперь: TipKbRouteNotOk на skip → `need process_id=` (и peers). SoftFL invent REJECT. Не Hold.

**live dogfood** dual hard `0.5.675` `build_utc=2026-08-06T18:43:02Z`:
- `kb facet=world get_process` → `kb memory_world get_process need process_id=` ack=0/1 (pulse no longer null)

