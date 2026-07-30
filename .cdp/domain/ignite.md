# Domain card: AutoIgnition (ignite / CDT)

- id: `ignite`
- organ: `cdp_ignite` / `IdeIgniteArmHost` + `IdeIgniteChannel`
- product: `#CDP`

## Invariants

- Composer charge default `minimal` + amnesia postfix; TM body stays in Task Manager.
- Continuity supersede: only plain armed timers; protect `remount-wake-*`, `tool-wake-*`, event wakes (build/test/shell), mid-`firing`.
- `leaf-wake` stable id — next leaf replaces prior.
- `await_operator` = plateau latch; do not invent next epic; `op=resume` after operator pick.
- CDT page pick must be Cursor Agents composer (`ComposerScoped`), not md/editor tab.

## Entry

- `cdp_ignite` · `IdeIgniteArmHost.*` · `IdeIgniteChannel.PagePick|Cdt|Fire`

## Antipatterns

- Re-arming continuity timer over remount/firing.
- Pasting TM stage names/commands into `message=` / custom charge without need.

## last_ship

- 0.5.306–308 page pick + remount/event protect; 0.5.309 leaf-wake chain
- 0.5.310 remount charge appends Domain pulse [A]
