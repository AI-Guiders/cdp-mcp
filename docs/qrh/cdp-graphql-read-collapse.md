# QRH — GraphQL read collapse (ADR-0233 L3)

**When:** agent needs situational read (search / peek / goto / diagnostics / session / git pulse / knowledge pack+corpus / project·sln list / health·recent·canon).

**Do:**
1. `cdp_graphql op=voyager` or `op=examples` — map + goldens 1–12.
2. `cdp_graphql op=query query='{ … }'` — one GraphQL document; hits carry `anchor { wire }`.
3. Empty project → honest null / empty ok (empty≠error). Real faults stay GraphQL errors.
4. Project/sln reads: `{ project { list scene recent } sln { list projects } }` (flat `projectScene`/`recent`/`health` aliases remain).
5. Knowledge pack: `{ knowledge { recall packs definition procedure process radiusGate } }` — not bare `memory_world_get_*`.

**Don't:** reach for bare `find` / `find_in_files` / `cdp_peek` / `cdp_search` / `cdp_goto` / `get_diagnostics` / `cdp_health` / `cdp_recent` / `cdp_project_list` / `cdp_sln_*` read / `memory_world_get_*` / `memory_world_recall_knowledge` / `memory_world_list_pack` / `memory_world_radius_gate_check` — unmounted from ListTools same release. Engines remain in-proc for GraphQL resolvers. Soft QRH `find-via-desk` + ECL `find-desk` point at `cdp_graphql`.

**Mutate unchanged:** CSX / `cdp_edit_plan` / `cdp_buffer` / sniper / build·test execute / git commit / `cdp_peel` (soft ListTools; CallTool/go=peel ok) / knowledge write·delete.
