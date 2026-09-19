# CDP-ADR-0233 — GraphQL query facade (read collapse)

**Status:** Accepted  
**Date:** 2026-09-19  
**Extends:** ADR-0198 (handlers in CdpService) · ADR-0230 (ListTools amortisation) · ADR-0009 (search organ → superseded for agent read)

## Context

Agent read is fragmented across `find` / `find_in_files` / `cdp_search` / `cdp_peek` / diagnostics / git scene / knowledge recall / … — ListTools tax + silent-zero / multi-path failures (Vision-Exp DSH 2026-09-19). Portal-style LIKE only works with translators; EF.Functions.Like is MSSQL-shaped, not rg/SQLite.

## Decision

1. **Query-only HotChocolate** in CdpService: `POST /api/v1/cdp/graphql` (+ Banana Cake Pop/Voyager for humans). MCP thin organ `cdp_graphql` (ADR-0198):
   - `op=voyager|type|examples|query` — full `__schema` only on request.
2. **Type binding SSOT:** GraphQL types = existing C#/F# models (`Cdp.ScriptableIde.Anchor` via `ToSpan`/`ToWire`, `FindInFiles.Hit`, `IdeProblemsChannel.Row`, `GitScene`/`GitDiffScene`/`GitPreflight.Report`, `CorrespondenceResult`, `NavigationScene`, `KnowledgeRecallFacade` envelope, …). Promote anonymous→record only where typed inner missing.
3. **DoD:** hits carry `anchor: Anchor!`; diagnostics same locus; knowledge recall = layered parity with `memory_world_recall_knowledge`; **empty ≠ error** (silent-zero banned).
4. **LIKE:** Portal `like`/`nlike` filter AST + per-engine translators (rg / SQLite FTS / symbol) — not blind MSSQL LIKE.
5. **Hard collapse (L3):** after L2 green, unmount read tools from ListTools **and** ship QRH/ECL/tool-desc in the **same** release. Mutate stays CSX / `cdp_edit_plan` / `cdp_buffer` / sniper / build·test execute / git commit.
6. **Agent ergonomics:** Did you mean + availableFields on unknown field; slim `first`/preview defaults; goldens from real flows + Vision-Exp external/ignore/OOW peek.

## Type-binding table (L0)

| GraphQL field / type | Engine / C# SSOT |
|----------------------|------------------|
| `Anchor` | `Cdp.ScriptableIde.Anchor` via `AnchorType` (`wire`/`file`/`lineStart`/`lineEnd`) |
| `textHits` → `TextHitConnection`/`TextHitNode` | `FindInFiles` / IdeFindChannel |
| `peek` → `PeekResult` | `CdpPeekChannel` |
| `diagnostics` → `DiagnosticNode` | `IdeProblemsChannel.Row` |
| `goto` → `GotoHitNode` | `cdp_goto` / navigation |
| `session` → `SessionNode` | `SessionContext` (honest nulls) |
| `correspondence` → `CorrespondenceResult` | `Correspondence` organ |
| `git` → `GitSceneNode` | `git_git_scene` via `DispatchToolAsync` |
| `knowledge` → `KnowledgeRecallResult` | `memory_world_recall_knowledge` parity |
| LIKE `like`/`nlike` | `LikeToRg` (rg); not MSSQL `EF.Functions.Like` |

Deferred (voyager hint only): `semanticMap`, `packages`, `testScene`, `symbol`.

## Nested ship

| Layer | DoD |
|-------|-----|
| L0 | this ADR + type-binding table |
| L1 | HC + schema roots + Anchor + LIKE skeleton + voyager/DidYouMean |
| L2 | resolvers→engines + KB parity + goldens 1–8 |
| L3 | hard unmount read + QRH same release |
| L4 | verify + deploy |

## Consequences

**+** One agent read surface; ListTools context freed.  
**−** Model inertia until QRH ships with unmount.  
**−** LIKE translators are required work, not a one-liner.

## Non-goals

- GraphQL Mutation day-one  
- Engine rewrite  
- Compat dual API for unmounted tools  
