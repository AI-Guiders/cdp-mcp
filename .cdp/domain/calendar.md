# Domain card: Calendar / local clock

- id: `calendar`
- organ: `cdp_calendar`
- product: `#CDP`

## Invariants

- Clock is **host machine local** (`DateTimeOffset.Now`), not UTC-only agent wall and not chat-guessed daypart.
- Cockpit slim always carries `clock=` pulse card (date, time, TZ, daypart, deadlines).
- Plan (`go=plan`) pulse/banner/focus also carry `local=` / deadlines — stage wall clock remains Start→Completed.
- Soft organ ops: `scene|pulse|month` (ASCII Mon-first grid, today `[dd]`).

## Entry

- Meta `cdp_calendar` · `go=calendar|clock|local_clock`
- Cockpit root `clock=` (no go needed for daypart)

## Antipatterns

- Inferring “morning/night” from conversation tone.
- Confusing stage wall elapsed with host calendar day.

## last_ship

- first ship + plan expand @ 0.5.486 · 2026-08-02
