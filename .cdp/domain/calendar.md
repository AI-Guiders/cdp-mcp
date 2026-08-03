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
- Citizen `@intent calendar|clock|calendar_desk` → host-execute IdeCalendarChannel (scene|pulse|month)
- Cockpit root `clock=` (no go needed for daypart)

## Antipatterns

- Inferring “morning/night” from conversation tone.
- Confusing stage wall elapsed with host calendar day.
- Treating `go=calendar` as host-execute — place-only; peer needs `@intent calendar`.

## last_ship

- 0.5.589: `@intent calendar|clock` → IdeCalendarChannel · dogfood ack=4/4 · VL #96 · 2026-08-03
- 0.5.551: sick_leave_dense deadline 05.08 beside citizen_chain 15.08 · 2026-08-03
- first ship + plan expand @ 0.5.486 · 2026-08-02
