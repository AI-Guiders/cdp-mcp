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
- HILD (default ARMED): Voice/empty 5s → `human_away` **once** (latch until Composer text); wake → autonomous; after wake continuity **1–2s** not 45m; suppress under `await_operator`.
- After successful fire: watch Cursor for "Connection Problems" / Try again|Retry overlay until next fire; auto-click (not Idle-only).
- After successful fire: also Win32-click Electron stall dialog "The window is not responding" → **Keep Waiting** (not OS hung dialog; not CDT).

## Entry

- `cdp_ignite` · `IdeIgniteArmHost.*` · `IdeIgniteChannel.PagePick|Cdt|Fire|Connection` · `IdeIgniteNativeDialogs`
- Cursor rule: `harness-checkpoint-automation.mdc`

## Antipatterns

- Re-arming continuity timer over remount/firing.
- Pasting TM stage names/commands into `message=` / custom charge without need.
- Treating invent-ban / `await_operator` as license to idle while operator away.
- Closing last leaf → plateau while overnight authorized.
- Writing another playbook for Connection Problems / stall dialog — fix is harness organ (`ConnectionWatch` + `NativeDialogs`).
- Confusing VS Code stall (Reopen/Close/Keep Waiting) with Windows "End task" hung dialog.

## last_ship

- 0.5.306–308 page pick + remount/event protect; 0.5.309 leaf-wake chain
- 0.5.310 remount charge appends Domain pulse [A]
- 2026-07-31: Autonomous Continuity Contract stamped
- 0.5.314: autonomous latch default ARMED — auto LeafPlateau → seed-wake (not await_operator); op=autonomous[_on|_off]
- 0.5.315: Connection Problems Retry watch after fire + dismiss during idle/send waits
- 0.5.316: Electron stall dialog Keep Waiting (Win32) in same post-fire watch
- 0.5.317: Connection Problems button label = Try again (was Retry-only match)
- 0.5.320: HILD — Composer idle 5s on Voice → human_away once → AutoI (op=hild)
- 0.5.321: HILD once-latch until human text (no thrash); post-wake continuity 1–2s not 45m

