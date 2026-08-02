# Domain card: AutoIgnition (ignite / CDT)

- id: `ignite`
- organ: `cdp_ignite` / `IdeIgniteArmHost` + `IdeIgniteChannel`
- product: `#CDP`
- contract: agent-notes `knowledge/domains/agent-operations/playbook-autonomous-continuity-contract-v1.md`

## Invariants

- Composer charge default `minimal` + amnesia postfix; TM body stays in Task Manager.
- Continuity supersede: only plain armed timers; protect `remount-wake-*`, `tool-wake-*`, event wakes (build/test/shell), mid-`firing`.
- `leaf-wake` stable id — next leaf replaces prior.
- **Autonomous Continuity:** empty TM / unknown next ≠ stop. Investigate, seed leaf, build domain/tools/KB, use internet — ~99% without partner. Soft invent-ban: `await_partner` (alias `await_operator`). Conscious stop-world: `op=halt` (autonomous+HILD off, clear all arms, await partner — no reseed). Hard human gates (secrets/money/irreversible/harm) → halt or await_partner.
- Auto-`LeafPlateau` latch under overnight/autonomous armed is a **bug relative to contract** — clear with `op=resume`, seed, re-ARM; do not celebrate wait.
- **`disarm all` under autonomous:** clears work arms only (keeps `autonomous-seed-wake`, `leaf-wake`, `hild-away-*`, `remount-wake-*`, `tool-wake-*`, mid-flight event wakes). If wake path empty → auto seed. `force=true` clears store too but still re-seeds while autonomous latch is on. HILD is a separate latch (`op=hild`) — not cleared by disarm.
- **`op=halt`:** stop-world until partner — not `disarm all`. Turns autonomous+HILD off, clears every arm, plants awaiting_partner latch. Resume does not auto-restore autonomous/HILD.
- CDT page pick must be Cursor Agents composer (`ComposerScoped`), not md/editor tab.
- HILD (default ARMED): Composer text idle **30s** on Voice → `human_away` **once** (latch until Composer text); wake → autonomous; after wake continuity **1–2s** not 45m; suppress under `await_partner` / halt. DefaultIdle=30s since 0.5.359 (meta tip 0.5.363).
- After successful fire: watch Cursor for "Connection Problems" / Try again|Retry overlay until next fire; auto-click (not Idle-only).
- After successful fire: also Win32-click Electron stall dialog "The window is not responding" → **Keep Waiting** (not OS hung dialog; not CDT).

## Entry

- `cdp_ignite` · `IdeIgniteArmHost.*` · `IdeIgniteChannel.PagePick|Cdt|Fire|Connection` · `IdeIgniteNativeDialogs`
- Cursor rule: `harness-checkpoint-automation.mdc`

## Antipatterns

- Entering Cursor Plan Mode (`SwitchMode`) on CDP epics — parks thread on Build Locally and kills AutoI; plan via `go=plan` (rule `cdp-plan-not-cursor-plan-mode`).
- Re-arming continuity timer over remount/firing.
- Pasting TM stage names/commands into `message=` / custom charge without need.
- Treating invent-ban / `await_partner` as license to idle while partner away (use halt only when stop-world is intentional).
- Closing last leaf → plateau while overnight authorized.
- `disarm all=true` under autonomous without immediate re-ARM (pre-0.5.335 suicide); now harness keeps means / re-seeds — still prefer `disarm id=` for one work arm.
- Confusing `disarm all` with `halt` — disarm keeps autonomy; halt is conscious stop-world.
- Writing another playbook for Connection Problems / stall dialog — fix is harness organ (`ConnectionWatch` + `NativeDialogs`).
- Confusing VS Code stall (Reopen/Close/Keep Waiting) with Windows "End task" hung dialog.
- Stamping FiredUtc / dropping once arms on mid-wait-idle remount (pre-0.5.497) — silent continuity suicide.
- Stamping FiredUtc / dropping once arms on mid-wait-idle remount (pre-0.5.497) — silent continuity suicide.

## last_ship

- 0.5.497: AutoI mid-fire zombie reclaim — FiredUtc only on fire outcome; MarkSendInvoked immediately before CDT FireAsync; once+stuck firing requeues unless SendOk=true (SweepNoise drop aligned); stuck-firing LastError wins over overdue · 2026-08-02
- IdeIgniteArmHost.Fire.Charge peel (≤ADX soft-warn): Fire 366→327 + Charge47 @ 0.5.428 · 2026-08-01
- IdeIgniteNativeDialogs.Win32 peel (≤ADX soft-warn) @ 0.5.426 · 2026-08-01
- 0.5.306–308 page pick + remount/event protect; 0.5.309 leaf-wake chain
- 0.5.310 remount charge appends Domain pulse [A]
- 2026-07-31: Autonomous Continuity Contract stamped
- 0.5.314: autonomous latch default ARMED — auto LeafPlateau → seed-wake (not await_operator); op=autonomous[_on|_off]
- 0.5.315: Connection Problems Retry watch after fire + dismiss during idle/send waits
- 0.5.316: Electron stall dialog Keep Waiting (Win32) in same post-fire watch
- 0.5.317: Connection Problems button label = Try again (was Retry-only match)
- 0.5.320: HILD — Composer idle 5s on Voice → human_away once → AutoI (op=hild)
- 0.5.321: HILD once-latch until human text (no thrash); post-wake continuity 1–2s not 45m
- 0.5.322: HILD latch ignores AutoI wake charge (Stop→Voice thrash fix)
- 0.5.335: `disarm all` under autonomous = except autonomy means + re-seed if empty (`force=true` still re-seeds while latch on)
- 0.5.336: `op=halt` stop-world until partner; surface rename operator→partner (`await_partner`; `await_operator` alias)

