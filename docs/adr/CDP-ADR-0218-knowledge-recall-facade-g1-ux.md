# CDP-ADR-0218: Unified KB recall facade (`recall_knowledge`) + G1 UX

**Status:** Accepted  
**Date:** 2026-09-12  
**Extends:** [CDP-ADR-0210](CDP-ADR-0210-knowledge-corpus-recall-federated.md)  
**WORK leaf:** `agent-notes/knowledge/domains/agent-operations/WORK-CDP-ADR-0218-recall-knowledge-facade-g1-ux.md`

## Problem

ADR-0210 fixed federated corpus recall, but agents still call `memory_session_search_agent_notes` for KB topics. Zero hot hits reads as «no data», not «wrong layer» (G1 cognitive).

## Decision

### 1. New tool `recall_knowledge` (world facet only)

MCP name: `memory_world_recall_knowledge`.

Args: `query` (required), `layer` (`auto`|`corpus`|`hot`, default `auto`), `limit` (1–50), `active_scope`, `primary_project_id`, `scope_only`, `workspace_path` (hot only).

### 2. `layer=auto` pipeline (deterministic)

1. corpus `mode=resolve` → if known && hits → return (`layers_used`: corpus, resolve)
2. corpus `mode=lookup` → if hits → return (corpus, lookup)
3. corpus `mode=search` → if hits → return (corpus, search)
4. hot `search_agent_notes` — only when corpus empty; skip hot when corpus hit

### 3. Unified JSON (merged `hits[]`)

Not passthrough of child tool JSON. Corpus hits: `layer:corpus`, `kind:lookup|search|resolve`. Hot hits: `layer:hot`, `kind:grep`, `line`, `text`.

### 4. G1 UX (same leaf)

- Empty hot search → `layer`, `searched`, `hint`, `try_next: { tool: memory_world_recall_knowledge }`
- Affordances: `recall_knowledge` on MemoryWorld; `search_agent_notes` Session-only (no Kb object)
- Citizen: free `query=` → `recall_knowledge`; `search_hot` → session search
- Tool descriptions steer agents to facade

## Non-goals

v2 pre-write gate, F# Recall contract, breaking renames, external HCI FTS.

## Verification

- `RecallKnowledgeFacadeTests`, `CitizenKbHostTests`, `KnowledgeCorpusRecallSmokeTests` (0210 regression)
- Live smoke: `memory_world_recall_knowledge query=ai-incidents`, session search 0 + try_next
