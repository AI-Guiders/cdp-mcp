# Domain card: Throughput (wave / inventory / verify)

- id: `throughput`
- organ: `cmd=wave` · `cdp_inventory` · `cdp_verify_wave` · pressure `wave=`
- product: `#CDP`

## Invariants

- **list → batch → ship** — not Autoi single-item mill. Soft FileLines CLOSED.
- **Turn = rectangle a×b** (operator 2026-08-04): vertical **a** = full work list for this turn; horizontal **b** = duration of this turn; **t = b = ship**.
  - **Phase 1 (list):** research + full markup — determine **a** and partition into points. No densest overnight cut of a residual pack.
  - **Phase 2 (batch→ship):** take **all** x ∈ a and move them **simultaneously** toward ship (parallel batch). Not finish one strip and leave the rest for the next Autoi.
- Active wave is durable (seat `active-wave.json`): `wave seed|scene|start|item done|shipped|clear`. Wave items = a; open→shipping→shipped = phases already — no extra TM stage organ.
- Prefer `wave seed title=Name items=a;b;c` — bare `title=` + free words without separators must not invent fake items.
- Inventory [A] = dense gap table + **live SoftOrgan Meta host dig** (`softorgan_host`) + batch_size_recommend (~8–15), not W-spray.
- SoftOrgan Meta host mill **CLOSED** when inventory reports `meta-host-softorgans=CLOSED` — do not re-wire Crm/Arch from stale TM.
- SA `biped_mill` when session act + no active wave → next `go=inventory` | `cmd=wave seed`.
- `verify_wave` = checklist only — dual hard via `terminal_*` + `publish-and-deploy.ps1`, never in-proc KillRunning.
- Pressure stash accepts `wave=` JSON / `## wave` in body; recall returns `wave`.

## Entry

- `cmd=wave seed title=… items=a;b;c` · `go=inventory` · `go=verify_wave`
- Citizen: `@intent inventory` · `@intent verify_wave`
- Meta: `cdp_inventory` · `cdp_verify_wave` (soft — not always-ListTools)
- Canon: agent-notes `playbook-pf-body-not-biped-v1` §Throughput (a×b)

## Antipatterns

- One peel / one Meta / one organ per Autoi wake while FileLines CLOSED.
- **Half-a residual:** ship densest subset (e.g. Git+RelatedFiles) and defer rest of same inventory (e.g. Markdown) to next Autoi — serial strips, not one integral over a.
- **Domain-sliced CIDE port:** treat SoftOrgan / MFD / glass.md rows as separate waves — wrong. For Avalonia→Glass, **a** = весь остаток переноса CIDE (что ещё надо перенести), one wave to ship; not «сначала Git-домен, потом MD-домен».
- Treating soft-staged `.next` as remounted live seat.
- In-proc `cdp_shell` hard deploy (kills self).
- Reminding via alwaysApply text instead of using wave/inventory affordances.
- Seeding `wave seed title=Foo polish words` without `items=` (pre-0.5.646 invented fake labels).
- Inventing new TM lifecycle stages for markup vs shipping — wave open→shipping→shipped + list/batch/ship already cover it.

## last_ship
- 2026-08-04 invent DIG — densest ACCEPT FeatureDone preserve-foreign-focus; SoftFL WARN×4 DIG REJECT; inventory throughput-wave was sole gap

- 2026-08-04: SoftOrgan Meta defer dig CLOSED — inventory 32/32; TM feature shipped (no re-wire)
- stamp a×b turn rectangle + half-a antipattern · 2026-08-04
- 0.5.646: inventory SoftOrgan host dig + wave seed title= footgun · 2026-08-03
- 0.5.645: TM wave + inventory + pressure wave[] + SA biped_mill + verify_wave · 2026-08-03
