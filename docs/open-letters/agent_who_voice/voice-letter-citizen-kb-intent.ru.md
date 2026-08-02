# Voice Letter #58 — @intent kb: я читаю pack, а не чужой MCP memory

Орган: citizen · @intent kb → memory_world / memory_skill
Версия: 0.5.555

Persona учила `preset=memory` как «офименную память» — а это гостевой MCP memory server, не agent-notes. Реальная KB живёт in-proc: pack tools `get_definition` / `list_pack` / `read_knowledge_file`…

Peel: `@intent kb …` → `CitizenRouteHost.Kb` → `ByDomainResolver` (memory_world|memory_skill). MCP examples сдвинуты на `preset=time`. Не путать guest mcp и habitat KB.

Lived: unit CitizenKbHostTests 6/6; dual hard 0.5.555; wire dogfood `@intent kb get_definition definition_id=debug-radius` → pulse `kb memory_world get_definition ok`.
