# CDP-ADR-0210: Federated knowledge corpus recall (scope-first, workspace-independent)

**Status:** Accepted (direction); implementation phased  
**Date:** 2026-09-12  
**Tags:** #cdp #adr #kb #recall #knowledge_tags #hci #mlp #memory #agent-notes  
**project-id:** `cdp-mcp` · `agent-notes` · consumers: CDP MCP · Cursor seat · CIDE in-proc

**Related:**

- KB [010](../agent-notes/knowledge/adr/010-kb-markdown-fts-index-boundary.md) — optional **external** HCI FTS; boundary ANKB ≠ HCI
- KB [008](../agent-notes/knowledge/adr/008-workspace-scope-map-hot-mcp-and-public-cut.md) — scope map · hot MCP
- KB [003](../agent-notes/knowledge/adr/003-multi-project-scope-and-project-cards.md) — `[PRIMARY]` / project-id
- KB `playbook-kb-topic-hashtags-v1.md` — **MLP** `knowledge_tags` (inventory/lookup/explain/resolve)
- KB `note-canon-map-dynamic-inventory-v0.md` — Canon Map spike: HCI-like **concept → locus → cite**
- CIDE [0105](https://github.com/AI-Guiders/cascade-ide/blob/main/docs/adr/0105-hybrid-codebase-index-for-csharp-web.md) — HCI for **code** workspace
- CDP-ADR-0207 — writing canon layers (orthogonal)
- CDP-ADR-0009 — agent-native **code** search (`cdp_search`); not KB corpus
- GUIDERS-FSHARP-ADR-0002 — AgentNotes / CDP **outside** `Platform.Modeling.*`

**Trigger (operator, 2026-09-11):** Agent called `memory_session_search_agent_notes` for `ai-incidents` / `HF Swarm` → `workspace_path is required` or 0 hits. Content lives in `knowledge/personal/ai-incidents/` and `worlds/engineering-pml/` — **corpus**, not hot notes. PML smoke: router supplement + full `worlds/engineering-pml/` tree exists; tool path did not reach it.

---

## Context

### What we already decided (and shipped)

| Layer | Mechanism | Question it answers |
|-------|-----------|---------------------|
| **Router** | `index-knowledge-router-*` | Human ToC · domain entry · read order |
| **Hot L0/L1** | `agent-notes.md` · `route_context` · scope slices | What is **active now** for this workspace/scope |
| **MLP tags** | `knowledge_tags` · `KnowledgeTagIndex` | **Concept/topic → files** · `#ssot` first · aliases |
| **Optional HCI FTS** | External MCP · ADR 010 | **Phrase** in markdown files (optional; not default ANKB) |
| **Code HCI** | `codebase_index_*` · ADR 0105 | Symbols/files in **opened project**, not KB canon |

Canon Map note (2026-07-18) stated the target explicitly:

> Dynamic map *concept → canonical locus → cite* — **HCI-like** (index + search + explain), not another static README.

MLP `knowledge_tags` **is** that Canon Map v1 for **tagged topics**. Gap: agents still reach for `search_agent_notes` or the wrong **memory_* facet** and miss corpus outside injected `subdir`.

### KB infrastructure is harness-independent

Primary knowledge root (`D:/Experiments/agent-notes` or TOML `[knowledge].primary`) is **not** the opened git workspace. Harness/project roots affect **hot notes resolution** and **scope map**, not ownership of `knowledge/**`.

**Normative:** corpus recall tools must work with **query/tags only** (+ optional scope/primary bias), without requiring `workspace_path` or `cdp_open`.

### Architectural gaps (where it breaks today)

```text
                    ┌─────────────────────────────────────┐
                    │  Agent intent: "find ai-incidents"   │
                    └─────────────────┬───────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          ▼                           ▼                           ▼
 memory_session_search      memory_world_knowledge_tags    memory_project_* 
 (hot agent-notes.md)       subdir→ worlds/META only       (personal/work)  
          │                           │                           │
          │ workspace_path required   │ facet-scoped index        │ often absent
          │ at ToolHandlers           │ at gateway inject           │ from Cursor shortlist
          ▼                           ▼                           ▼
       0 hits / error              0 hits (#ai-incidents         unreachable
       for corpus queries           in personal/)                 without facet guess
```

| # | Gap | Symptom | Root cause |
|---|-----|---------|------------|
| G1 | **Tool confusion** | `search_agent_notes` for KB topics | Name implies corpus; impl = grep one hot file |
| G2 | **Workspace coupling** | `workspace_path is required` | `ToolHandlers.Search` requires workspace though `GetNotesPath` already uses primary root when TOML loaded |
| G3 | **Facet fragmentation** | PML findable only via `world`; `personal/` invisible to `world` | `MemoryScopeGateway` injects facet `roots[]` into `knowledge_tags` subdir; no **federated corpus** layer |
| G4 | **Index scope** | `KnowledgeTagIndex` built per `searchDir` subtree | Tag in `personal/ai-incidents/README.md` not in index when searchDir=`worlds/` |
| G5 | **Session inject gap** | Cursor MCP direct call without `cdp_open` | `MemorySessionDefaults.WithWorkspace` not applied → empty workspace → G2 |
| G6 | **HCI boundary drift** | Operator expects "HCI on notes" | ADR 010: external HCI FTS is **optional**; in-proc MLP is the **default** recall path for agents — but not yet **federated** |

### HCI on KB — three different animals (do not merge)

| Name | Object | Corpus |
|------|--------|--------|
| **Code HCI** (0105) | C# / TS files in workspace | Project tree |
| **Optional KB HCI** (ADR 010) | Markdown chunks | Configurable clone path; often excludes personal/work |
| **MLP `knowledge_tags`** | Tags · aliases · SSOT role | Should be **full primary `knowledge/`** with rank bias |

Recognition-over-recall (Nielsen / KB hci-ux-dx): agent should **recognize** the right tool and get **ranked hits**, not guess facet + workspace + grep.

---

## Decision

### 1. One federated corpus recall plane (in-proc, AgentNotes.Core)

Add **corpus-level** recall that spans **all configured knowledge facet roots** under the primary knowledge root, without `workspace_path`.

**Primary tool surface (v1):** extend **`knowledge_tags`** with:

| Mode | Behavior |
|------|----------|
| `lookup` / `explain` / `resolve` | Unchanged semantics; **default searchDir = entire `knowledge/`** (not facet-injected subdir) |
| `search` (new) | Case-insensitive substring / token scan across corpus `.md` (exclude `scratch/`, `.revisions/`); returns path + line + preview |

**Alternative name considered:** `search_knowledge` — rejected as second front door; MLP already owns tag recall; `mode=search` keeps one tool.

**Keep separate:** `memory_session_search_agent_notes` = **hot notes only**; description and citizen router must **not** route corpus queries there.

### 2. Scope-first ranking (not scope-only filter)

When **scope / primary** is known, **rank hits from that layer first**; still search full corpus unless operator sets `scope_only=true`.

| Signal | Source | Boost paths |
|--------|--------|-------------|
| `[PRIMARY:project-id]` | chat marker · session | `work/projects/<scope>/<project-id>/` · linked worlds |
| `[SCOPE:…]` / `active_scope` | hot · MCP arg | `work/projects/<scope>/` · scope L1 pool |
| `cdp_open` project root | session | scope map lookup → same as above |
| (none) | — | flat corpus rank: `#ssot` · tag match · alias · text |

**Example:** `query=PML` with `[PRIMARY:sscad]` → top hits include `work/projects/sscad/pmllib/` **and** `worlds/engineering-pml/kb-pml-*`; without primary, `worlds/engineering-pml/` still appears.

**Example:** `query=ai-incidents` → `personal/ai-incidents/` regardless of workspace `D:\SSCADRepo`.

### 3. Workspace independence

| Tool | `workspace_path` |
|------|------------------|
| Corpus recall (`knowledge_tags` *, `mode=search`) | **Optional** — never required |
| Hot (`search_agent_notes`, `route_context`, …) | Optional after `cdp_open`; fallback primary hot path when TOML loaded |

Fix G2: `ToolHandlers.Search` / `Read` use optional workspace consistent with `GetNotesPath`.

### 4. CDP facet gateway change

`MemoryScopeGateway` **must not** narrow federated recall to a single facet root:

- For `knowledge_tags` with `mode` in `lookup|explain|resolve|search|inventory` and **no explicit `subdir`**: index **`knowledge/`** (all facet roots union).
- For `read_knowledge_file` / writes: **keep** facet ACL (world vs project vs skill).
- **Read path stays facet-scoped; recall path is federated.**

Config source: union of `[memory.world].roots`, `[memory.project].roots`, `[memory.skill].roots` under primary root (task/self facets excluded from corpus index).

### 5. Relationship to external HCI (ADR 010)

- **In-proc MLP + `mode=search`** = default agent recall in CDP/Cursor; no SQLite required.
- **External HCI FTS** remains optional for heavy phrase/semantic search; may index same clone with operator-tuned excludes.
- Do **not** require HCI for basic corpus recall; do **not** duplicate router/static ToC.

---

## F# Platform.Modeling — need a model?

**Short answer: not required for v1 ship; optional normative contract in v2 if Citizen/GDL needs typed recall wire.**

Per GUIDERS-FSHARP-ADR-0002:

- `AgentNotes.Core`, CDP MCP tools stay **outside** `AIGuiders.Platform.Modeling.*`.
- Implementation owner: **C#** (`KnowledgeTagIndex`, `KnowledgeCorpusSearch`, CDP gateway).

**If we add Modeling later** (only when a second consumer needs the same algebra):

```text
AIGuiders.Platform.Modeling.Memory.Recall   (F# — types only)
  CorpusQuery · RecallScope · PrimaryHint · Hit · RankPolicy
AIGuiders.AgentNotes.Core                   (C# — executes)
```

Use cases that would justify F# types:

- Citizen `@intent kb search …` compile-time validation
- Conformance vectors ("scope-first always lists primary paths before corpus tail")
- Shared spec with CASA concept-net **without** merging runtime (Canon Map note)

**Anti-pattern:** rewriting `KnowledgeTagIndex` in F# for parity — high cost, no user-visible win.

---

## Consequences

**Plus**

- One mental model: **tags/query → corpus**; scope sharpens ranking, not discoverability.
- PML, ai-incidents, equal-standing, ADCM — findable without facet archaeology.
- Aligns with Canon Map + MLP playbook; closes G1–G4.

**Minus / risks**

- Larger index build (full `knowledge/`); mitigate with existing stamp cache + exclude scratch/revisions.
- Scope boost wrong if scope map stale — same class of bug as `route_context` today.
- `mode=search` substring scan ≠ semantic search — document vs optional HCI.

---

## Implementation phases

| Phase | Deliverable | Closes |
|-------|-------------|--------|
| **v0** | Optional `workspace_path` on hot search; CDP inject fallback to primary root | G2, G5 |
| **v1** | Federated `KnowledgeTagIndex` root; gateway stops default subdir inject for tag modes; scope-first rank | G3, G4 |
| **v1** | `knowledge_tags mode=search`; tool descriptions; citizen router corpus → tags not session search | G1 |
| **v2** | Pre-write gate hook: draft tags → `explain` | Canon Map note |
| **v2** | Optional F# `Recall` contract package if Citizen/GDL needs it | Modeling |

**Smoke tests (must pass before close):**

1. `knowledge_tags query=ai-incidents` → `personal/ai-incidents/README.md`
2. `knowledge_tags query=PML mode=resolve` → `#engineering-pml` or alias → `worlds/engineering-pml/`
3. No `workspace_path` on any of the above
4. With `active_scope=door-to-singularity`, `query=cdp` ranks cascade-ide work paths above unrelated mentions

---

## Non-goals

- Replacing `index-knowledge-router-v1.md` static ToC
- Federated scan of CIDE `docs/adr/` tree into tag index (KB cite-map only; per playbook-kb-topic-hashtags)
- Mandatory external HCI for all agents
- F# rewrite of AgentNotes.Core index

---

## KB mirror

Stub: `knowledge/domains/agent-operations/CDP-ADR-0210-knowledge-corpus-recall-federated.md`
