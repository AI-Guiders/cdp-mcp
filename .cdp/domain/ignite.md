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
- **`disarm` under autonomous:** work-arm remove that empties wake path → seed (`removed > 0`); noop missing-id (`removed=0`) does **not** plant Guest Autoi CDT seed (0.5.535). Re-ARM `last_once` via `arm` supersede — do not disarm→arm ritual.
- **`autonomous-seed-wake` fire:** if TM already has an incomplete leaf mid-window, suppress Guest Autoi CDT and redirect to `leaf-wake` (`TrySuppressAutonomousSeedBeforeDelivery` · `board_has_incomplete_leaf` · 0.5.536) — LeafPlateau race after `done` before next leaf lands.
- Guest Autoi remains CDT→Composer adapter (ADR-0025) for system wakes **and** for autonomous plain timers when PF is not duplex-live: `prefer_autonomous` stamps habitat SSOT then CDT fallthrough (0.5.532). **Cursor host dogfood (0.5.554):** invite ready must **not** steal this path — Composer is the gun while the agent sits in Cursor.
- **Citizen Autoi consume (0.5.551→0.5.554):** only when Composer unavailable (`TryDeliverHabitatWhenComposerUnavailableAsync` · gone/down) + `invite_ready` → `TryDeliverAutoiWake` · `detail=prefer_citizen` · Intercom `kind=citizen`. Idle PF + invite while Composer present → Guest CDT→Composer.
- **Habitat prefer skip-CDT** on plain timer work arms when PF duplex busy|composing (`ShouldPreferHabitatDelivery` duplex · `detail=prefer_duplex`).
- Partner-mode (autonomous off) + idle PF on plain timer work arms: Intercom mirror (`MirrorTimerWakeToIntercom`) so Glass sees charge — Composer fallthrough when Voice/idle.
- Remount wakes (`remount-wake-*`), HILD escalate (`hild-escalate-*`), plain HILD away (`hild-away` / `hild-away-*`), OOM (`oom-wake-*`), and tool-wake (`tool-wake-*`): always Intercom mirror (`detail=remount_intercom`|`escalate_intercom`|`hild_intercom`|`oom_intercom`|`tool_intercom`) even when PF busy|composing; prefer habitat stays off (CDT→Composer fallthrough intact). Event wakes (build/test/shell): no mirror.
- Continuity wakes (remount / escalate / hild-away / OOM / tool-wake) + Composer Stop/Queue **or** gone (`no_composer`/`down` / sample fail): habitat deliver + skip CDT even if Intercom mirror missed (`MayDeliverHabitatWhenComposerUnavailable` · `TryDeliverHabitatWhenComposerUnavailableAsync` · `detail=*_composer_busy`|`*_composer_gone`). **Guest Autoi exception (0.5.547):** plain timer + autonomous + idle PF + Stop/Queue → do **not** habitat-success (`ShouldHabitatSkipWhenComposerUnavailable`) — CDT wait / `busy_timeout`→requeue (habitat SSOT already stamped by prefer_autonomous). composer_gone still habitat. Duplex busy|composing still skip. build/test/shell: no habitat. Voice/send Composer: CDT fallthrough.
- **last_once under autonomous:** successful fire (habitat or CDT) must **not** latch `awaiting_partner` (`ShouldLatchAwaitingPartnerAfterSuccessfulFire`) — ACC invent-ban; Remove arm + seed if wake path empty (`last_once_delivered_autonomous`). last_once without autonomous still awaits partner. ADX `LastOnceFireAwaitingOk` + ArmPath/Meta tip match runtime (no invent-ban teaching under autonomous). Arm/leaf tips under autonomous: last_once + leaf-wake are **insurance if thread dies** — NOT permission to park while a TM leaf is started (`LastOnceArmHint` · `ArmForLeafHint` · 0.5.537).
- **Timer busy requeue:** `ShouldRequeueBusy` for timer includes `busy_timeout` / `no_agent_composer` / `wrong_surface` / **`click_failed`** (0.5.549) — CDT Send click miss must not leave last_once as dead error arm.
- **Stale error reclaim (0.5.550):** `ReclaimOverdue` + `SweepNoise` revive `status=error` when `ShouldRequeueBusy` would apply (`reclaimed_error_*` / `hygiene_requeue_*`) — tombstones from pre-requeue era.
- Composer-unavailable habitat skip also publishes Intercom charge (`PublishHabitatIntercomCharge`) — Glass parity with prefer duplex when mirror miss (0.5.529).
- Wake charge SSOT: `%LocalAppData%/cdp-mcp/ignite-wake-LATEST.json` (`composer`|`habitat`) — Composer is not the only spine for charge body.
- CDT page pick must be Cursor Agents composer (`ComposerScoped`), not md/editor tab.
- HILD (default ARMED): Composer text idle **30s** on Voice → `human_away` **once** (latch until Composer text); wake → autonomous; on edge/escalate **pull-forward** armed last_once work timers ≤3s (`PullForwardLongWorkTimersOnHildAway`); arm under `away_latched` clamps ≤3s; after wake continuity **1–3s** not 45m; suppress under `await_partner` / halt. DefaultIdle=30s since 0.5.359 (meta tip 0.5.363).
- **last_once arm clamp under autonomous:** ≤3m by default; **≤3s** when HILD `away_latched` **or** TM `ContinuityFlight.Fly` (`3s(hild_away)` / `3s(leaf_started)` · `force=true` escape).
- **Already-armed last_once:** HILD away edge/escalate pull-forward ≤3s; under autonomous + leaf Fly, TimerLoop also pull-forwards (`3s(leaf_pull)`).
- After successful fire: watch Cursor for "Connection Problems" / Try again|Retry overlay until next fire; auto-click (not Idle-only).
- After successful fire: also Win32-click Electron stall dialog "The window is not responding" → **Keep Waiting** (not OS hung dialog; not CDT).

## Entry

- `cdp_ignite` · `IdeIgniteArmHost.*` · `IdeIgniteChannel.PagePick|Cdt|Fire|Connection` · `IdeIgniteNativeDialogs`
- **Citizen peer path (0.5.566):** `@intent ignite|autoi …` host-executes the same channel (arm/disarm/list/continuity/resume). `go=ignite*` still place-only.
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
- Digging CDT-down from health `cdt=?` (pre-0.5.498 PulseLine never live-sampled).
- Dual-seat twin OOM wake (pre-0.5.499) — both click dialog + schedule → twin `no_agent_composer` silent once-drop.
- Recover Not-connected zombie without remount-wake pending (pre-0.5.503 opt-in `-StampRemountPending`) — silent no Autoi initialized wake after remount.

- Leaving Meta `cdp_ignite` tip as Composer-only Autoi spine after habitat prefer / Guest CDT fallthrough ships (pre-0.5.533) — invent-ban hygiene; tip must match runtime.
- Noop `disarm id=` (`removed=0`) under autonomous planting `autonomous-seed-wake` (pre-0.5.535) — Guest Autoi CDT thrash mid re-ARM; re-ARM via `arm` supersede, not disarm→arm.
- LeafPlateau `autonomous-seed-wake` firing Guest Autoi while next incomplete leaf already landed mid-window (pre-0.5.536) — CDT thrash with wrong "seed next leaf" charge; fire-time recheck → `leaf-wake`.
- Arming `last_once` then parking on the timer while a TM leaf is **started** under autonomous (pre-0.5.537) — tips taught `End turn` / `before idle`; ACC: insurance ≠ idle license when partner away.
- Continuity scene `next_step=wait for event` + canonical charge `re-arm when idle` under autonomous (pre-0.5.538) — same park teaching via arms.explain / Composer wake body.
- Arming `last_once in=45m` under autonomous (pre-0.5.539) — accidental end-turn looks like "working" for half an hour; clamp ≤3m (`ClampAutonomousLastOnceInsurance`; `force=true` escape).
- HILD ARMED but long `last_once` still parks after away edge (pre-0.5.542) — HILD watched partner, not agent park; pull-forward ≤3s + arm clamp when `away_latched`.
- Autonomous + TM leaf Fly + long `last_once` while partner **here** (pre-0.5.543) — ≤3m still allowed agent-park; clamp to `3s(leaf_started)` when `ProbeFlight()==Fly`.
- Leaving Meta `cdp_ignite` tip at ≤3m-only after leaf Fly / HILD ≤3s ships (pre-0.5.544) — invent-ban hygiene residual.
- Arm clamp alone while a long last_once was already armed before leaf Fly (pre-0.5.545) — TimerLoop pull-forward `3s(leaf_pull)` under autonomous+Fly.
- Leaving Channel XML tip at tip parity 0.5.537 after leaf Fly clamp/pull ships (pre-0.5.546) — invent-ban hygiene; Channel summary must match Meta (≤3s leaf Fly / HILD; TimerLoop pull-forward).
- Guest Autoi overnight + idle PF + Composer Stop/Queue habitat-success skip consuming last_once (pre-0.5.547) — lived `leaf-wake` habitat latch, arms=[], operator «выстрела нет»; CDT must wait/requeue.

## last_ship

- 0.5.566: citizen `@intent ignite|autoi` host-execute → `IdeIgniteChannel.Handle` (peer re-ARM without Cursor MCP) · VL #73 · 2026-08-03
- 0.5.551: Citizen Autoi consume habitat wake (`prefer_citizen` · TryDeliverAutoiWake) + sick_leave_dense deadline to 05.08 · 2026-08-03
- 0.5.550: Reclaim/hygiene revive status=error when ShouldRequeueBusy (click_failed tombstones) · 2026-08-03
- 0.5.549: ShouldRequeueBusy includes click_failed (CDT Send miss → backoff requeue) · 2026-08-03
- 0.5.547: Guest Autoi Stop ≠ habitat-success (`ShouldHabitatSkipWhenComposerUnavailable` · CDT wait/requeue) · VL #50 · 2026-08-03
- 0.5.546: Channel XML tip parity — leaf Fly / HILD ≤3s + TimerLoop pull-forward (residual after Meta 0.5.545) · VL #49 · 2026-08-03
- 0.5.545: leaf Fly TimerLoop pull-forward armed last_once → ≤3s (`PullForwardLongWorkTimersOnLeafFly` · `3s(leaf_pull)`) · VL #48 · 2026-08-02
- 0.5.544: Meta `cdp_ignite` tip parity — leaf Fly / HILD ≤3s clamp (residual after 0.5.543) · 2026-08-02
- 0.5.543: leaf Fly arm clamp last_once → ≤3s (`leafFlying` · `3s(leaf_started)`) · VL #47 · 2026-08-02
- 0.5.542: HILD away pull-forward long last_once → ≤3s + arm clamp under away_latched (`PullForwardLongWorkTimersOnHildAway` · `HildAwayContinuityMax`) · VL #46 · 2026-08-02
- 0.5.541: ArmPath event + QRH tips under ACC — keep flying / in=3s habit (not end-turn park)
- 0.5.539: autonomous last_once timer clamp ≤3m (`ClampAutonomousLastOnceInsurance`) · VL #43 · 2026-08-02
- 0.5.538: ContinuityExplain + CanonicalComposerCharge under ACC — timer/insurance ≠ park (`ContinuityArmedNextStep` · charge rewrite) · VL #42 · 2026-08-02
- 0.5.537: ArmPath/LeafChain/Meta tips under autonomous — last_once + leaf-wake insurance ≠ park while TM leaf started (`LastOnceArmHint` · `ArmForLeafHint`) · VL #41 · 2026-08-02
- 0.5.536: autonomous-seed fire rechecks TM incomplete leaf → suppress CDT + redirect `leaf-wake` (`TrySuppressAutonomousSeedBeforeDelivery`) · VL #40 · 2026-08-02
- 0.5.535: noop disarm under autonomous does not plant Guest Autoi seed (`removed > 0` gate) · VL #39 · 2026-08-02
- 0.5.534: IdeIgniteChannel XML summary tip parity with Meta habitat prefer (residual Composer-only doc after 0.5.533) · 2026-08-02
- 0.5.533: Meta `cdp_ignite` tip parity — duplex prefer skip-CDT · autonomous stamp + Guest CDT fallthrough · wake latch SSOT (residual after 0.5.532) · VL #38 · 2026-08-02
- 0.5.532: prefer_autonomous stamps habitat SSOT but Guest Autoi CDT fallthrough (`IsHabitatLatchForArm` keeps latch) — fix ACC silent after 0.5.531 · VL #37 · 2026-08-02
- 0.5.531: habitat prefer under autonomous on plain timers even when PF idle (`ShouldPreferHabitatDelivery` · `prefer_autonomous`) — overnight skip CDT residual · VL #36 · 2026-08-02
- 0.5.530: ignite `last_once` schema property tip matches autonomous no invent-ban (residual after VL #35) · 2026-08-02
- 0.5.529: ADX last_once autonomous parity + ArmPath/Meta tip + unavailable Intercom duplex (`LastOnceFireAwaitingOk` · `PublishHabitatIntercomCharge`) · VL #35 · 2026-08-02
- 0.5.528: last_once under autonomous ≠ awaiting invent-ban (`ShouldLatchAwaitingPartnerAfterSuccessfulFire` · seed `last_once_delivered_autonomous`) · VL #34 · 2026-08-02
- 0.5.527: Composer unavailable habitat skip **without** Intercom mirror required (`MayDeliverHabitatWhenComposerUnavailable` · Voice Publish miss residual) · VL #33 · 2026-08-02
- 0.5.526: Intercom mirrored + Composer gone/down → habitat skip CDT (`ShouldSkipCdtAfterIntercomMirror` · `*_composer_gone`) · VL #32 · 2026-08-02
- 0.5.525: plain HILD away Intercom mirror + Composer Stop skip CDT (`IsHildAwayWakeArm` · `hild_intercom`|`hild_composer_busy`) · VL #31 · 2026-08-02
- 0.5.524: tool-wake Intercom mirror + Composer Stop skip CDT (`IsToolWakeArmId` · `tool_intercom`|`tool_composer_busy`) · VL #30 · 2026-08-02
- 0.5.523: OOM Intercom mirror + Composer Stop skip CDT (`IsOomWakeArm` · `oom_intercom`|`oom_composer_busy`) · VL #29 · 2026-08-02
- 0.5.522: HILD escalate Intercom mirror + Composer Stop skip CDT (`IsHildEscalateWakeArm` · `escalate_intercom`|`escalate_composer_busy`) · VL #28 · 2026-08-02
- 0.5.520: Intercom mirrored (idle-PF or remount) + Composer Stop/Queue → habitat skip CDT (`TryDeliverMirroredWhenComposerBusyAsync` · `idle_pf_composer_busy`|`remount_composer_busy`) · VL #26 · 2026-08-02
- 0.5.519: remount + Composer Stop/Queue → habitat deliver + skip CDT (`TryDeliverRemountWhenComposerBusyAsync` · `remount_composer_busy`); Voice/idle still CDT fallthrough · VL #25 · 2026-08-02
- 0.5.518: remount Intercom mirror always (`IsRemountWakeArm` · `remount_intercom`) even when PF busy; prefer habitat still off · VL #24 · 2026-08-02
- 0.5.516: Glass Autoi wake consumer companion — hydrate `ignite-wake-LATEST` on cockpit_host start · VL #22 · 2026-08-02
- 0.5.515: idle-PF Intercom mirror on plain timer work arms (Composer fallthrough; remount/OOM/HILD/event excluded) · VL #21 · 2026-08-02
- 0.5.503: Recover-CdpSeatRemount stamps remount-wake pending by default (parity with hard deploy; `-NoStampRemountPending` opt-out) · 2026-08-02
- 0.5.501: Composer/CDT host-surface tooth — `ignite-wake-LATEST` SSOT + prefer habitat (intercom) over CDT when PF presence busy|composing on plain timer arms (remount/OOM/HILD/event stay Composer adapter) · 2026-08-02
- 0.5.499: OOM tooth crooked fix — IdeOomCrossProcessClaim (dual-seat twin wake); ShouldRequeueBusy includes no_agent_composer/wrong_surface; DefaultDueSeconds=20 · 2026-08-02
- 0.5.498: teeth CDT auto-refresh on health pulse + Recover-CdpSeatRemount.ps1 (Not-connected zombie) · 2026-08-02
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

