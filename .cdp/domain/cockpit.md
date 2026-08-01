# Domain card: Cockpit desk (seats P|F|M)

- id: `cockpit`
- organ: `cdp_cockpit`
- product: `#CDP` `#CIDE`

## Invariants

- Dark Cockpit [A] default: pulse seats, not W spray (`seats_detail=full` alone refused).
- `go=` soft organs; `cmd=` REPL into plan/shell/….
- `pane_full=` / `go_detail=full` = one C dump; habit stay A.
- Seats sticky in WitDB; cold tools may auto-restore desk bookmark.

## Entry

- `cdp_cockpit` · desk seats · soft organs via `go=`

## Antipatterns

- W-spray for “understanding the desk”.
- Treating slim pulse absence of mutation fields as “op failed” without `go_detail=full`.

## last_ship

- soft-warn: `IdeRepl` partials (Desk/Organs/Board/Ops/Share/Crm/Helpers) @ 0.5.393 — see `iderepl.md`
- ADR-0020 peels / pane_full pulse path (see board Glass epic)
- soft-warn: `IdeDeskSeats` → `IdeDeskSeats.Placement.cs` (ResolveSeat→Opt) @ 0.5.377; main~364
