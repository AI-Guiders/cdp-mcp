---
type: ADR
title: 'CDP-ADR-0230: D:\Experiments\Personal Cursor Folder\open\cdp mcp\docs\adr\CDP ADR 0230 listtools amortisation'
adr_id: cdp-mcp/CDP-ADR-0230-listtools-amortisation
status: stable
adr_status: 'Accepted'
date: 2026-09-19
---

# CDP-ADR-0230 — ListTools amortisation (invalidate vs cache)

**Epic:** Habitat AxB Attractors L4

## Context

ADR-0202 already bumps `capabilitiesRev` and emits `tools/list_changed`. Cold ListTools still paid full HTTP + VisibleToolCatalog recompose on every host refresh even when rev was unchanged.

## Decision

1. **Bridge cache** (`CdpBridgeToolsCache`): key = `capabilitiesRev`; invalidate on watcher rev bump (list_changed still fires).
2. **Service cache** (`VisibleToolsSessionCache`): key = `(rev, phase, object, language, intent)`; invalidate on `NotifyListChanged`.
3. **Meta catalog** (`MetaToolCatalog.All`): build once per process; `Build()` clones the immutable list.
4. **Soft-hide** more soft instruments from always-ListTools (`cdp_ignite`, `cdp_pressure`, …) — CallTool / `go=` remain.
5. **Optional** `GET /api/v1/cdp/capabilities?rev=N` → `{ capabilitiesRev, unchanged: true }` when N matches (no tools[] body).

## Consequences

- Repeat ListTools at same rev is cache hit (bridge and/or service).
- Phase/object change still recomposes (fingerprint includes session).
- Deploy / NotifyListChanged still invalidates correctly.

## Non-goals

- CsxHelpCatalog XML index (deferred).
- Changing CallTool routing for soft instruments.
