# Domain: freshness (KB freshness MLP)

**Desk:** `cdp_freshness` · `go=freshness|freshness_desk|kb_freshness`  
**Status:** W2–W4 + OOA&D types · SoftInstrument / desk lexicon (not organ)

## Invariants

- **Types:** Channel = facade; Catalog · Watchlist · Probe · Desk · Nrt · Schedule · Cache · Feed — real types, not SoftFL partials (`partial ≠ split`).
- Harness walks watchlist URLs; agent receives **Digest** (`freshness_digest/v1`), not raw HTML walls.
- **Digest ≠ `Проверено:`** — never auto-write `knowledge/`; agent digs then stamps via `memory_world_*`.
- Fingerprints: ETag / Last-Modified / Atom-RSS latest id / body SHA-256; cache under seat state (`freshness-cache.json`).
- Soft-instrument shortlist: use `go=freshness` or CallTool `cdp_freshness` (not always cold ListTools).
- **NRT:** `op=nrt` peels `status-*` Next review / Revisit triggers; scan rows may carry `nrt.fire_suggested` when fingerprint changed.
- **Schedule:** `op=arm when=nightly|in=` · `op=tick` (opt-in; not background spam of entire canon).

## Entry

1. `cdp_freshness op=scene` — MLP ops + waves  
2. `op=aliases` — built-ins (`baseline2026`, `php`, `laravel`, `avalonia`, `node`)  
3. `op=watchlist alias=` / `domain=` / `urls=`  
4. `op=scan|digest` → entries with `changed` (+ `nrt`)  
5. `op=nrt alias=` / `domain=` — status triggers  
6. `op=clear` / `op=arm` / `op=tick`  
7. Agent: dig deltas → stamp evidence/status

## Antipatterns

- Claiming full-a freshness from digest alone  
- Shell-grep mill instead of this desk  
- Mounting standalone AgentNotesMcp for read — Core already in-proc (`memory_world_*`)  
- Background spam of entire canon (timer is opt-in)  
- Saying «орган» for CDP desks — SoftInstrument / desk / scene

## Waves

| Wave | Scope |
|------|--------|
| **W1** | scene/watchlist/scan/digest/explain/aliases + cache |
| **W2** | richer explain + status NRT fire heuristics + cache clear |
| **W3** | timer / nightly digest arm + tick |
| **W4** | dogfood hot domains → Проверено stamps |

## last_ship

- **0.5.718** — OOA&D: Catalog/Watchlist/Probe/Desk/Nrt types; Channel facade; SoftFL partials removed (2026-08-15)
- **0.5.717** — W2–W4 clear/nrt/schedule/tick + tests + domain card (2026-08-15)
- **0.5.716** — W1 `cdp_freshness` desk + tests + domain card (2026-08-15)
