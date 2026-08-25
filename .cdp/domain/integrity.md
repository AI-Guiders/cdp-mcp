# Domain: habitat integrity (HIS)

**Desk (v1):** `cdp_integrity` · `go=integrity|integrity_desk`  
**ADR:** `docs/adr/CDP-ADR-0206-habitat-integrity-subsystem.md`  
**Status:** spec accepted · v0 piggyback on peek/buffer · desk not shipped

## Invariants

- **Not harm POST** (`integrity-core`) · **not awareness POST** — **content/trust integrity** of artifacts agent cites.
- **No static canon file list** — Trust Registry grows on attest; dynamic `artifact_key → fingerprint`.
- **Detect, don't block** operator disk edit — drift flag in tool results; reconcile via git / operator_declared.
- **`cdp_freshness` = external URL leg** of same subsystem (`provenance=external_digest`).
- Drift without reconcile → agent must not silent-cite (parallel auto-poisoning).

## Provenance channels

`agent_write` · `git_commit` · `operator_declared` · `host_bypass` · `external_digest` · `unknown`

## Entry (v0 → v1)

**v0 (today):** read ADR; on drift suspicion → `git status` / `git diff` on path; prefer `cdp_peek` over host Read.

**v1 (planned):**

1. `cdp_integrity op=scene`
2. `op=check path=` / `scope=git_dirty|registry`
3. `op=attest` after agent write (auto on buffer flush)
4. `op=reconcile` — git + operator signals
5. `op=quarantine path=` — handoff to retract/rebuild

Piggyback: `cdp_peek` / `cdp_buffer` responses include `integrity` block when registry present.

## Antipatterns

- Maintaining a handwritten «SSOT paths» markdown list as integrity system
- Citing KB from context window without re-read after long gap
- Treating `cdp_freshness` digest as «Проверено» without local attest
- Confusing HIS with jailbreak / harm integrity

## Siblings

| Domain | Role |
|--------|------|
| `freshness` | URL fingerprints → `external_digest` |
| `buffer` / `disk_peek` | buffer↔disk leg |
| `pressure` | `integrity·{status}` on organ (planned) |
| `quarantine` | plugins only today — not KB |

## last_ship

- **ADR-0206** — subsystem spec locked (2026-08-25)
