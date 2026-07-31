# Domain card: Pressure desk (L1 continuity)

- id: `pressure`
- organ: `cdp_pressure` / `IdePressureChannel`
- product: `#CDP`

## Invariants

- L1 notify → `op=arm` → checklist → `op=stash body=` (no export ritual to operator).
- `op=stash` also appends **memo line** (`pressure-memo.jsonl`) — anti-compaction archive.
- `op=memo` / `op=line` — write/read agent konspekt history (not raw transcript).
- Must axes: AutoIgnition re-ARM, Task Manager, Habitat=CDP, **Domain** (`.cdp/domain`).
- Hot stash last-wins; memo line append-only. After compact: `op=recall` + `op=line`.
- Remount Autoi charge (0.5.310+) appends Domain pulse [A] when cards exist.

## Entry

- `cdp_pressure` · `go=pressure_desk` · `IdeDomainPulse`

## Antipatterns

- Offering export/checkpoint ritual on L1.
- Stashing without Domain when domain work is in flight.
- Trusting host compaction summary over memo line.

## last_ship

- 0.5.318 memo line (`op=memo|line`); stash auto-appends
