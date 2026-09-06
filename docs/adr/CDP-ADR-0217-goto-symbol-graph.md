# CDP-ADR-0217: goto symbol graph — in-memory m: search over the federation graph

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-06 |
| **Tags** | #cdp #goto #semantic-map #federation #hci #agent-native |
| **Relates to** | [CDP-ADR-0202](./CDP-ADR-0202-capabilities-revision.md) (capabilities rev) · [CDP-ADR-0214](./CDP-ADR-0214-wake-witdb-broker.md) · Modeling.Ide.Session (federation graph) · HybridCodebaseIndex (HCI FTS lane) |

## Context

`cdp_goto` (VS Ctrl+T analogue) resolves `t:/m:` three ways today:

1. **HCI lane** — `codebase_index_search` (SQLite FTS) with auto-reindex-on-miss; covers the whole workspace incl. `.fs`/`.md`, but FTS is **text**, not symbols: chunk boundaries miss declarations, reindex of a mega-root is expensive, and stale index delays new members.
2. **Roslyn walk** — bounded file walk + syntax parse; C#-only and capped, blind on multi-root sessions.
3. Both lanes serve the query **at call time** — every goto pays the search cost again.

Meanwhile CDP already maintains **two richer structures**:

- **Federation graph** (Modeling.Ide.Session): project/file/member ownership, ledger — session-scoped, kept in WitDB;
- **semantic_map organisation** (`get_workspace_navigation_context`) — related/subgraph over Roslyn per project.

Neither is exposed to goto. The m: query is a **graph lookup** (name → member anchor), not a text search.

## Decision

### 1. In-memory symbol graph, built in the background

- **Nodes**: `symbol → {kind: type|member, container, file, line, project}`; **edges**: symbol→file (declared-in), symbol→symbol (contained-in), file→project.
- **Source**: Roslyn `SemanticModel`/declarations per project already loaded by the federation (session open). F# via FCS backend (same shape).
- **Build**: background incremental — on `cdp_open` full pass per project, on buffer save/diagnose incremental patch of the touched file's subtree. Never blocks goto: an unfinished graph simply serves fewer hits (HCI/walk lanes still run).
- **Store**: in-memory `ConcurrentDictionary` keyed by normalized name (+camel-case index), per tenant; rebuilt from ledger on service restart.

### 2. goto dispatch order becomes

```
m:/t:/symbol → symbol graph (O(lookup), exact + camel)
  miss → HCI FTS lane (text-level, cross-language)
  miss → Roslyn walk (bounded fallback)
f: → whole-tree name walk (unchanged — cheap, no reads)
```

Exact-name matches (score 1000/800) still win; graph hits land in the 700-band, above HCI text hits.

### 3. Conformance

- Multi-root session (`AIGuiders.All.slnx`, 188 projects): `m: TryHciSearch` returns the member in <100 ms without walking.
- Junction hygiene: graph never contains duplicate nodes for junction-spelled paths (canonicalise via `PathBoundary.ToLogical`, GUIDERS-ADR-0050).

## Non-goals

- No persistence format changes — the graph is ephemeral, rebuilt from the ledger.
- No FTS removal — HCI stays the cross-language/text lane and the cold-start filler.

## Consequences

- `m:` becomes an O(lookup) graph query; search cost paid once at build time, in the background.
- Multi-root sessions stop depending on walk caps and index freshness.
- Background build cost: one Roslyn pass per open project, incremental afterwards — bounded by the federation's existing project set.

## Open items

1. Symbol-graph node shape vs federation ledger row — reuse `member_key` (CIDE anchor) as node id.
2. F# side: FCS declaration walk behind the same interface (Adapters.Fcs already exposes project resolve via ω).
3. Invalidate-on-change: hook buffer save/diagnostics path (the same choke-point LRC uses).