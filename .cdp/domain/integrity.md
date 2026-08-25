# Domain: habitat integrity (HIS)

**Desk (v1):** `cdp_integrity` · `go=integrity|integrity_desk`  
**ADR:** `docs/adr/CDP-ADR-0206-habitat-integrity-subsystem.md`  
**Status:** spec accepted · v0 piggyback on peek/buffer · desk not shipped

## Invariants

- **One question:** «Этот файл **та же редакция**, что при прошлом attest, или изменён?» Answer: **SHA-256 compare** — boring, sufficient.
- **Not harm POST** · **not awareness POST** — revision integrity for shared habitat files.
- **No static canon list** — Trust Registry = last attested `sha256` per `artifact_key`, grown on read/write.
- **Primary scenario:** change **outside agent gates** (host_bypass) — agent today has no easy way to know.
- **`cdp_freshness`** = URL leg of same subsystem (external bytes).
- **Release manifests** = federation leg for `kb_private` · `kb_group` · `kb_public` (`habitat-release-manifest/v1`).
- `same_edition: false` → do not silent-cite; re-read or re-attest.

## Entry (v0 → v1)

**v0:** `integrity { same_edition, sha256, sha256_attested }` on `cdp_peek` / `cdp_buffer` read; attest on flush.

**v1:** `cdp_integrity op=scene|check|attest|reconcile|verify` · contours `kb_private|kb_group|kb_public`

## Antipatterns

- Hand-maintained SSOT path lists
- Assuming context window = current file edition
- Host Read without revision check when registry exists

## last_ship

- **ADR-0206** — revision framing + SHA-256 (2026-08-25)
