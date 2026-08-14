# Domain: freshness (KB freshness MLP)

**Organ:** `cdp_freshness` · `go=freshness|freshness_desk|kb_freshness`  
**Status:** W1 shipped · MLP map (not mini-MVP)

## Invariants

- Harness walks watchlist URLs; agent receives **Digest** (`freshness_digest/v1`), not raw HTML walls.
- **Digest ≠ `Проверено:`** — never auto-write `knowledge/`; agent digs then stamps via `memory_world_*`.
- Fingerprints: ETag / Last-Modified / Atom-RSS latest id / body SHA-256; cache under seat state (`freshness-cache.json`).
- Soft-organ shortlist: use `go=freshness` or CallTool `cdp_freshness` (not always cold ListTools).

## Entry

1. `cdp_freshness op=scene` — MLP ops + waves  
2. `op=aliases` — built-ins (`baseline2026`, `php`, `laravel`, `avalonia`, `node`)  
3. `op=watchlist alias=` / `domain=` / `urls=`  
4. `op=scan|digest` → entries with `changed`  
5. Agent: dig deltas → stamp evidence/status

## Antipatterns

- Claiming full-a freshness from digest alone  
- Shell-grep mill instead of this organ  
- Mounting standalone AgentNotesMcp for read — Core already in-proc (`memory_world_*`)  
- Background spam of entire canon (W3 timer is opt-in later)

## Waves

| Wave | Scope |
|------|--------|
| **W1** | scene/watchlist/scan/digest/explain/aliases + cache |
| W2 | richer explain + status NRT fire heuristics |
| W3 | timer / nightly digest arm |
| W4 | dogfood hot domains → Проверено stamps |

## last_ship

- **0.5.716** — W1 `cdp_freshness` soft organ + tests + domain card (2026-08-15)
