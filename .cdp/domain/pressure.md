# Domain card: Pressure desk (L1 continuity)

- id: `pressure`
- organ: `cdp_pressure` / `IdePressureChannel`
- product: `#CDP`

## Invariants

- L1 notify → `op=arm` → checklist → `op=stash body=` (no export ritual to operator).
- Must axes: AutoIgnition re-ARM, Task Manager, Habitat=CDP, **Domain** (`.cdp/domain`).
- Durable stash survives remount; `op=recall` after compact.
- Remount Autoi charge (0.5.310+) appends Domain pulse [A] when cards exist.

## Entry

- `cdp_pressure` · `go=pressure_desk` · `IdeDomainPulse`

## Antipatterns

- Offering export/checkpoint ritual on L1.
- Stashing without Domain when domain work is in flight.

## last_ship

- 0.5.310 Domain axis + remount domain pulse
