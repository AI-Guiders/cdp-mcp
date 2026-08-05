# Domain card: Throughput (wave / inventory / verify)

- id: `throughput`
- organ: `cmd=wave` · `cdp_inventory` · `cdp_verify_wave` · pressure `wave=`
- product: `#CDP`

## Invariants

- **list → batch → ship** — not Autoi single-item mill.
- **Mill (operator 2026-08-05):** going one tiny piece per turn **where the work fits one axb**. Ban was on **wrong method**, not «SoftFL work forbidden». SoftFLStruckAxb that only dropped Hold / stamped CLOSED without file-order axb = board hygiene ≠ SoftFL ship. Real SoftFL = list FileLines gaps → one wave → ship.
- **Turn = rectangle a×b** (operator 2026-08-04): vertical **a** = full work list for this turn; horizontal **b** = duration of this turn; **t = b = ship**.
  - **Phase 1 (list):** research + full markup — determine **a** and partition into points. No densest overnight cut of a residual pack.
  - **Phase 2 (batch→ship):** take **all** x ∈ a and move them **simultaneously** toward ship (parallel batch). Not finish one strip and leave the rest for the next Autoi.
  - **Teeth (0.5.652):** focused `feature_done` under autonomous with no active wave → refuse (half-a). Escape: `force=true` or `wave seed` first.
- Active wave is durable (seat `active-wave.json`): `wave seed|scene|start|item done|shipped|clear`. Wave items = a; open→shipping→shipped = phases already — no extra TM stage organ.
- Prefer `wave seed title=Name items=a;b;c` — bare `title=` + free words without separators must not invent fake items.
- Prefer short `items=` labels without spaces, or quoted blobs; `items=` with spaces must not invent one item per whitespace token (0.5.650).
- Inventory [A] = dense gap table + **live SoftOrgan Meta host dig** (`softorgan_host`) + batch_size_recommend (~8–15), not W-spray.
- SoftOrgan Meta host mill **CLOSED** when inventory reports `meta-host-softorgans=CLOSED` — do not re-wire Crm/Arch from stale TM.
- SA `biped_mill` when session act + no active wave → next `go=inventory` | `cmd=wave seed`.
- `verify_wave` = checklist only — dual hard via `terminal_*` + `publish-and-deploy.ps1`, never in-proc KillRunning.
- Hard deploy remount nudge is **per-seat** (0.5.661) — sibling hard must not remount the live survivor (`CdpReloadNudge.ps1`).
- Pressure stash accepts `wave=` JSON / `## wave` in body; recall returns `wave`.

## Entry

- `cmd=wave seed title=… items=a;b;c` · `go=inventory` · `go=verify_wave`
- Citizen: `@intent inventory` · `@intent verify_wave`
- Meta: `cdp_inventory` · `cdp_verify_wave` (soft — not always-ListTools)
- Canon: agent-notes `playbook-pf-body-not-biped-v1` §Throughput (a×b)

## Antipatterns

- **Teeth naming (lie):** AutoI / FeatureDone half-a refuse is a **shield** (holds blow, seals course, latch/armor) — not «зубы». «Teeth» sounds like aggression and rhymes with «рубить дерево зубами» (imitation instead of axe). Prefer future rename shield|latch|course-guard; until then hold: щит ≠ зубы-имитация.
- One peel / one Meta / one organ per Autoi wake while FileLines CLOSED.
- **Half-a residual:** ship densest subset (e.g. Git+RelatedFiles) and defer rest of same inventory (e.g. Markdown) to next Autoi — serial strips, not one integral over a.
- **Domain-sliced CIDE port:** treat SoftOrgan / MFD / glass.md rows as separate waves — wrong. For Avalonia→Glass, **a** = весь остаток переноса CIDE (что ещё надо перенести), one wave to ship; not «сначала Git-домен, потом MD-домен».
- Treating soft-staged `.next` as remounted live seat.
- In-proc `cdp_shell` hard deploy (kills self).
- Global MCP nudge remounting both seats on sibling hard (pre-0.5.661).
- Reminding via alwaysApply text instead of using wave/inventory affordances.
- Seeding `wave seed title=Foo polish words` without `items=` (pre-0.5.646 invented fake labels).
- Inventing new TM lifecycle stages for markup vs shipping — wave open→shipping→shipped + list/batch/ship already cover it.

## last_ship
- **2026-08-05 SoftFLStruckAxb (board hygiene)** — dropped Hold SoftFL-REJECT + stamped «STRUCK»; **did not** run SoftFL file-order axb. Operator: mill = serial tiny peels where one axb fits; ban = wrong method, not work ban. SoftFL when needed = list→batch→ship files.
- **2026-08-05 DIG ACCEPT CitizenFullReadyAxb** — after plan-pulse tax CLOSED: inventory sole real gap=`throughput-wave` idle · SoftFL/Meta CLOSED · flight-durable already [x]. densest product residual=`Glass CIT peer ack 2/4` (citizen.md full-ready E2E). SoftFL BuildBoard REJECT. Wave seeded 6 items.
- **0.5.661** — per-seat remount nudge + orphan remount pending (sibling hard ≠ kill survivor seat) · 2026-08-04
- **2026-08-04 ProductDigCitizen15** — wave a×b: glass-done-close · dig-verdict · voice-letter-intercom-iop · citizen-hold · (bogus CCL `wave start` noop). DIG REJECT SoftFL mill; DIG ACCEPT invent=Voice Letter after cabin + hold to 15.08.
- **2026-08-04 cabin** — Intercom IOP live dogfood: Glass host rebuilt; surface `run action=topic_next` → `glass · topic next · …` (stale Release exe was unknown). Wave item glass-cabin-iop-dogfood.
- **0.5.652** — FeatureDone refuse half-a under autonomous + no active wave (`force=` escape). Teeth for a×b rectangle close. Tests IdeFeatureDoneAxbTests.
- **0.5.650** — wave `items=` with spaces no longer invents one fake item per whitespace token (lived: CitizenFullThenNext649 → 0/16). Collect into one blob until next key=; `;|,` still split labels. Test Repl_wave_seed_items_with_spaces_does_not_invent_word_items.
- **2026-08-04 SickLeaveNight648 mid** — ignite hygiene + glass DIG REJECT reopen; product dig PathMutate vs Autoi duplex seeded.
- **2026-08-04 invent DIG FindIntent648** — DIG REJECT SoftFL/Meta/Citizen/OOM-eol reopen; DIG ACCEPT densest = throughput-wave idle → SickLeaveNight648 (ignite stale-arms → CIDE/glass citizen15 → product wave).
- **2026-08-04 Dig FindIntent@0.5.648** — DIG REJECT SoftFL/Meta/Citizen reopen; DIG ACCEPT board CLOSED hygiene under FeatureDone@0.5.647 + FindIntent unique-prefix CLOSED; inventory sole gap = throughput-wave idle; BoardClosedHygiene648 1/4 (Meta defer/BATCH/SoftFL peel shipped; Dig FindIntent feature shipped).
- **2026-08-04 SoftFL CLOSED dig** — DIG REJECT SoftFL reopen (inventory CLOSED). densest ACCEPT: board CLOSED noise hygiene under FeatureDone @0.5.647 live dogfood; IntentSelect clears stage by design (restore in FeatureDone); dual `-Target` terminal habit ≠ code mill.
- 2026-08-04 invent DIG — densest ACCEPT FeatureDone preserve-foreign-focus; SoftFL WARN×4 DIG REJECT; inventory throughput-wave was sole gap

- 2026-08-04: SoftOrgan Meta defer dig CLOSED — inventory 32/32; TM feature shipped (no re-wire)
- stamp a×b turn rectangle + half-a antipattern · 2026-08-04
- 0.5.646: inventory SoftOrgan host dig + wave seed title= footgun · 2026-08-03
- 0.5.645: TM wave + inventory + pressure wave[] + SA biped_mill + verify_wave · 2026-08-03
