# Domain card: Throughput (wave / inventory / verify)

- id: `throughput`
- organ: `cmd=wave` · `cdp_inventory` · `cdp_verify_wave` · pressure `wave=`
- product: `#CDP`

## Invariants

- **list → batch → ship** — not Autoi single-item mill. Soft FileLines CLOSED.
- Active wave is durable (seat `active-wave.json`): `wave seed|scene|start|item done|shipped|clear`.
- Inventory [A] = dense gap table + batch_size_recommend (~8–15), not W-spray.
- SA `biped_mill` when session act + no active wave → next `go=inventory` | `cmd=wave seed`.
- `verify_wave` = checklist only — dual hard via `terminal_*` + `publish-and-deploy.ps1`, never in-proc KillRunning.
- Pressure stash accepts `wave=` JSON / `## wave` in body; recall returns `wave`.

## Entry

- `cmd=wave seed a;b;c` · `go=inventory` · `go=verify_wave`
- Citizen: `@intent inventory` · `@intent verify_wave`
- Meta: `cdp_inventory` · `cdp_verify_wave` (soft — not always-ListTools)

## Antipatterns

- One peel / one Meta / one organ per Autoi wake while FileLines CLOSED.
- Treating soft-staged `.next` as remounted live seat.
- In-proc `cdp_shell` hard deploy (kills self).
- Reminding via alwaysApply text instead of using wave/inventory affordances.

## last_ship

- 0.5.645: TM wave + inventory + pressure wave[] + SA biped_mill + verify_wave · 2026-08-03
