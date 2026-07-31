# Domain card: Pressure desk (L1 continuity)

- id: `pressure`
- organ: `cdp_pressure` / `IdePressureChannel`
- product: `#CDP`

## Invariants

- L1 notify → `op=arm` → checklist → `op=stash body=` (no export ritual to operator).
- `op=stash` also appends **memo line** (`pressure-memo.jsonl`) — anti-compaction archive.
- `op=memo` / `op=line` — write/read agent konspekt history (not raw transcript).
- Must axes: AutoIgnition re-ARM, Task Manager, Habitat=CDP, **Domain** (`.cdp/domain`).
- Hot stash last-wins; memo line append-only. After compact: `op=recall` (+ gate **pull**) → `op=reconcile` (self-steer) → `op=align` → `op=ready` · `op=line`.
- Remount Autoi charge (0.5.310+) appends Domain pulse [A] when cards exist.
- Anti-pattern: waiting for operator to name a slice when memo+README/TM already suffice (external locus).

## Entry

- `cdp_pressure` · `go=pressure_desk` · `IdeDomainPulse`

## Antipatterns

- Offering export/checkpoint ritual on L1.
- Stashing without Domain when domain work is in flight.
- Trusting host compaction summary over memo line.
- Skipping reconcile self-steer and inventing from stale Domain.

## last_ship

- 0.5.324 recall gate pull→reconcile→align→ready (CDP-ADR-0024)
