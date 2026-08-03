# Domain card: Cockpit desk (seats P|F|M)

- id: `cockpit`
- organ: `cdp_cockpit`
- product: `#CDP` `#CIDE`

## Invariants

- Dark Cockpit [A] default: pulse seats, not W spray (`seats_detail=full` alone refused).
- `go=` soft organs; `cmd=` REPL into plan/shell/….
- Slim desk always exposes host-local `clock=` (daypart + TZ + deadlines); `go=calendar` for month grid.
- `pane_full=` / `go_detail=full` = one C dump; habit stay A.
- Seats sticky in WitDB; cold tools may auto-restore desk bookmark.

## Entry

- `cdp_cockpit` · desk seats · soft organs via `go=`
- `go=calendar|clock` / Meta `cdp_calendar` · machine-local date/time

## Antipatterns

- W-spray for “understanding the desk”.
- Treating slim pulse absence of mutation fields as “op failed” without `go_detail=full`.
- Guessing daypart/date from chat — read `clock=` / `go=calendar` / plan `local=`.
- `go=plan` + nested `go_args.tm_op` without SoftBoard flatten (pre-0.5.552) — mutation=null; use top-level `cmd=` or flatten (shipped).

## last_ship

- live: `cdp_cockpit_host op=start` → dual-cockpit up · CascadeIDE · latches=32 · Standalone15 densest (was down/agent-only) · 2026-08-03
- 0.5.552: Plan SoftBoard `flattenOrganArgs` + `OptGoArg(tm_op)` — TM mute via go_args fixed · 2026-08-03
- local clock + calendar soft organ + plan pulse local expand @ 0.5.486 · 2026-08-02
- IdeDeskSeats.Presets peel (≤ADX soft-warn): main 365→267 + Presets104 @ 0.5.429 · 2026-08-01
- soft-warn: `IdeRepl` partials (Desk/Organs/Board/Ops/Share/Crm/Helpers) @ 0.5.393 — see `iderepl.md`
- ADR-0020 peels / pane_full pulse path (see board Glass epic)
- soft-warn: `IdeDeskSeats` → `IdeDeskSeats.Placement.cs` (ResolveSeat→Opt) @ 0.5.377; main~364
