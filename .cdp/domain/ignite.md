# Domain card: AutoIgnition (ignite / CDT)

- id: `ignite`
- organ: `cdp_ignite` / `IdeIgniteArmHost` + `IdeIgniteChannel`
- product: `#CDP`
- contract: agent-notes `knowledge/domains/agent-operations/playbook-autonomous-continuity-contract-v1.md`

## Invariants

- Composer charge default `minimal` + amnesia postfix; TM body stays in Task Manager.
- Continuity supersede: only plain armed timers; protect `remount-wake-*`, `tool-wake-*`, event wakes (build/test/shell), mid-`firing`.
- `leaf-wake` stable id — next leaf replaces prior.
- **Autonomous Continuity:** empty TM / unknown next ≠ stop. Investigate, seed leaf, build domain/tools/KB, use internet — ~99% without operator. `await_operator` only on explicit operator stop or hard human gate (secrets/money/irreversible/harm).
- Auto-`LeafPlateau` latch under overnight/autonomous armed is a **bug relative to contract** — clear with `op=resume`, seed, re-ARM; do not celebrate wait.
- CDT page pick must be Cursor Agents composer (`ComposerScoped`), not md/editor tab.

## Entry

- `cdp_ignite` · `IdeIgniteArmHost.*` · `IdeIgniteChannel.PagePick|Cdt|Fire`
- Cursor rule: `harness-checkpoint-automation.mdc`

## Antipatterns

- Re-arming continuity timer over remount/firing.
- Pasting TM stage names/commands into `message=` / custom charge without need.
- Treating invent-ban / `await_operator` as license to idle while operator away.
- Closing last leaf → plateau while overnight authorized.

## last_ship

- 0.5.306–308 page pick + remount/event protect; 0.5.309 leaf-wake chain
- 0.5.310 remount charge appends Domain pulse [A]
- 2026-07-31: Autonomous Continuity Contract stamped
- 0.5.314: autonomous latch default ARMED — auto LeafPlateau → seed-wake (not await_operator); op=autonomous[_on|_off]
