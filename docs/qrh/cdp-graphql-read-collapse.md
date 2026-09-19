# QRH — GraphQL read collapse (ADR-0233 L3)

**When:** agent needs situational read (search / peek / goto / diagnostics / session / git pulse / knowledge).

**Do:**
1. `cdp_graphql op=voyager` or `op=examples` — map + goldens 1–8.
2. `cdp_graphql op=query query='{ … }'` — one GraphQL document; hits carry `anchor { wire }`.
3. Empty project → honest null / empty ok (empty≠error). Real faults stay GraphQL errors.

**Don't:** reach for bare `find` / `find_in_files` / `cdp_peek` / `cdp_search` / `cdp_goto` / `get_diagnostics` — unmounted from ListTools same release. Engines remain in-proc for GraphQL resolvers.

**Mutate unchanged:** CSX / `cdp_edit_plan` / `cdp_buffer` / sniper / build·test execute / git commit.
