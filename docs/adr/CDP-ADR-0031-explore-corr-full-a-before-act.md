# CDP-ADR-0031: Explore corr full-a before Act

- **Status:** Accepted
- **Date:** 2026-08-14
- **Related:** CDP-ADR-0029 (analysis organ) · cascade-ide ADR 0155/0156 (CRS) · seeming Done shields

## Context

Agents wrote ADRs then skipped reading them (CIDE→Glass: «ready» ×4, actually 0). Explore that does not dig correspondence is **half-a** — Act on unread decisions.

Correspondence (`cdp_analysis_scene feature=correspondence`) already resolves `.cascade/workspace.toml` ADR maps. Without a tooth, the map is a pretty shelf.

## Decision

Three layers:

1. **Substrate** — keep hot loci in `[workspace.adr.map]` (Glass / analysis / pressure / mutate shields).
2. **Explore tooth** — `ExploreCorrLatch` + `ExploreCorrGate`: Mutate / buffer edit|create soft-refuse when locus has forward ADRs unless a fresh latch exists from successful corr dig **or** explicit `feature=no_adr why=`.
3. **Done** — human-faced ship (`IdeSeemingDoneShield`) refuses without a fresh latch when the workspace has `.cascade/workspace.toml` (ADR unread = seeming). `force=true` escape.

SA (`go=sa_desk`) returns `need_more` on ADR-mapped dirty paths without latch.

Disable: env `CDP_EXPLORE_CORR=off`.

## Consequences

- Full-a Explore is habitat-enforced, not another markdown reminder.
- Empty map (`"*" = []`) does not arm the mutate gate — populate maps for hot paths.
- cascade-ide `"*"` wildcard ADR still arms nearly all loci there — intentional for that repo.
