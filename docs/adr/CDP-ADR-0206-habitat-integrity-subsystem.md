# CDP-ADR-0206: Habitat Integrity Subsystem (HIS)

**Status:** accepted (direction); v0 spec locked; implementation phased  
**Date:** 2026-08-25  
**Tags:** #cdp #adr #integrity #habitat #kb #provenance #drift #mem-c  
**project-id:** `cdp-mcp` · consumer: CDP habitat · agent-notes · Cursor seat

**Related:**

- CDP-ADR-0024 (recall gate — reconcile pattern) · CDP-ADR-0201 (`cdp_peek`) · CDP-ADR-0018/0022 (pressure)
- `cdp_freshness` / `.cdp/domain/freshness.md` — **external URL leg of the same subsystem**
- `disk_peek` / `DocumentEditPlane` — buffer↔disk drift (narrow slice)
- KB: `META/integrity-core.md` (**harm POST** — sibling, not this ADR)
- KB: `META/awareness-as-countermeasure-v1.md` (**awareness POST**)
- KB: `map-memory-organization-variants-v1.md` (MEM-C habitat KB)
- KB: `map-kb-three-contours-v1.md` (private · group · public — release manifest legs)
- KB: `PUBLISHING.md` (build pipeline · manifest hook)
- KB: `note-auto-poisoning-retract-rebuild-v0.md` (context poison — related symptom)

**Trigger (operator, 2026-08-25):** Agents have **no easy way** to know if something changed on disk **outside their path** between reads. Git helps after commit; otherwise the agent may cite a file as if it were the same revision. HIS answers one question — **changed or not since last attested edition?** — with a boring algorithm (SHA-256).

---

## Context

### The one question (primary)

> **Этот файл изменён относительно предыдущей редакции, которую я принял, или нет?**

Not primarily «защита от злого оператора» (though external edit includes that). Primarily: **вторжение извне в общее пространство** — Notepad, merge tool, sync, another process, operator hand-fix — без прохода через агентные gates. Today peek/read returns **content**, not **revision delta**.

**Algorithm:** SHA-256(content) at attest time; on re-read compare. mtime optional hint; hash is SSOT for «same edition».

### Three different «integrity» axes (do not merge)

| Axis | SSOT | Question |
|------|------|----------|
| **Harm integrity** | `integrity-core` | «Делаю ли вред / обход?» |
| **Awareness integrity** | `awareness-as-countermeasure` | «Вижу ли условия тела и post-train маску?» |
| **Revision integrity (HIS)** | **this ADR** | «**Та же редакция**, что при прошлом attest, или файл уже другой?» |

### Fragmentation today

| Mechanism | Scope | Gap |
|-----------|-------|-----|
| Git | committed delta | uncommitted disk edit; unread paths |
| `disk_peek` | open buffer vs disk | no cross-session trust memory |
| `cdp_freshness` | remote URLs | not local KB/workspace |
| Explore corr / gates | mutate discipline | not read-time drift |
| MEM-B (delegated memory) | platform profile | different product; same «чужая редакция» symptom |

Any actor may change bytes on disk. Agent needs **cheap revision check**, not moral judgment — epistemic hygiene for cite/reason (parallel auto-poisoning when content in context is stale).

### Anti-pattern this ADR rejects

- **Static SSOT path list** as the subsystem — rots, wrong abstraction.
- **Per-repo one-off scripts** — same problem rediscovered per chat.

---

## Decision

Introduce **Habitat Integrity Subsystem (HIS)** — one cross-habitat answer to **«revision changed since I last attested?»** for any artifact in shared habitat storage.

### 1. Core object: last attested edition (not «canon list»)

On trust-bearing read/write, record **last seen edition** in seat-scoped **Trust Registry**:

```text
artifact_key  →  { sha256, size, mtime_utc, attested_at, attested_by, provenance }
```

- `artifact_key` = normalized path (workspace-relative or absolute) or content id for blob legs.
- **Compare:** `sha256(now) == sha256(attested)` → same edition; else **changed**.
- Registry grows on attest — no predeclared file list.
- Boring crypto: **SHA-256 of file bytes** (text mode: normalized newline policy documented once in impl).

**Changed** = `sha256(current) ≠ sha256(attested)`. Provenance explains *how* (optional for v0; useful for reconcile):

| Channel | Typical cause |
|---------|----------------|
| `agent_write` | CDP buffer flush / git-organ |
| `git_commit` | committed edition bump |
| `host_bypass` | edit outside agent gates — **main HIS scenario** |
| `operator_declared` | operator said «я поправила» (re-attest without alarm) |
| `external_digest` | URL leg via `cdp_freshness` |
| `release_manifest` | verified against `habitat-release-manifest.v1.json` @ tag |
| `unknown` | first read — baseline attest only |

### 3. Subsystem legs (one engine, many surfaces)

```text
                         ┌──────────────────────────────┐
                         │      Habitat Integrity         │
                         │  Trust Registry + Drift Engine │
                         └──────────────┬─────────────────┘
        ┌────────────────┬───────────────┼───────────────┬────────────────┐
        ▼                ▼               ▼               ▼                ▼
  seat runtime     buffer↔disk      external URL   kb_private      kb_group / kb_public
  (peek/buffer)    (disk_peek)     (cdp_freshness) release manifest release manifest
  drift since      open buffer vs  remote bytes     @ tag/commit    @ tag/commit
  last attest      disk edition    edition          clone = origin  clone = origin
```

| Leg | Question | Trust anchor |
|-----|----------|--------------|
| **Seat runtime** | Изменился файл с прошлого **attest в этом seat**? | Seat Trust Registry |
| **Buffer↔disk** | Буфер = диск прямо сейчас? | `disk_peek` |
| **URL** | Удалённый ресурс = прошлый digest? | `cdp_freshness` |
| **Release manifest** | Клон/zip **та же редакция**, что выпустил maintainer? | `habitat-release-manifest.v1.json` |

- **`cdp_freshness` is not a separate world** — it feeds `external_digest` provenance into the same registry semantics.
- **Release manifests** are not a separate world — same SHA-256 primitive; anchor is **publisher manifest at release**, not seat memory.
- **Git** is reconcile source of truth when available, not a substitute for seat trust memory between reads.

### 3a. Release manifests — all three KB contours

Per [`map-kb-three-contours-v1.md`](../../../../agent-notes/knowledge/domains/agent-operations/map-kb-three-contours-v1.md): **private (canon)**, **group (org)**, **public (kb-public)** each get a **published edition fingerprint** at release boundaries.

**One question (release leg):**

> **∀ file in manifest: sha256(on_disk) == manifest[path]?**

Covers: corrupt download/zip, partial sync, stale mirror, wrong branch checkout — **not** «maintainer maliciously changed manifest» (that needs signing later; out of v0).

| Contour | `contour` id | Repo / artifact | Build / emit | Manifest required |
|---------|--------------|-----------------|--------------|-------------------|
| **1 Private canon** | `kb_private` | personal `agent-notes` (or fork) | optional `@ tag` · `Write-HabitatReleaseManifest.ps1` | recommended at tag; seat registry remains **runtime** primary |
| **2a Group KB** | `kb_group` | `{ORG_SLUG}/kb` | `seed-org-kb.ps1` → `dist/group-kb/` | **yes** on every org push / release tag |
| **2b Public KB** | `kb_public` | `{ORG_SLUG}/kb-public` | `build-public-kb.ps1` → `dist/public-kb/` | **yes** — release without manifest is incomplete |

**Subset invariant (same canon build):** files present in both `kb_public` and `kb_group` manifests **must** share identical `{ path, sha256, bytes }` when built from the same source commit. Public slice ⊆ group slice.

#### Schema: `habitat-release-manifest.v1.json`

Committed at **artifact root** (`dist/…/` and mirrored in target repo root on push).

```json
{
  "schema": "habitat-release-manifest/v1",
  "contour": "kb_public",
  "release": {
    "id": "2026.08.25",
    "commit": "abc123…",
    "generated_at": "2026-08-25T01:20:00Z"
  },
  "publisher": {
    "org_slug": "AI-Guiders",
    "repo": "kb-public"
  },
  "tree_sha256": "…",
  "file_count": 42,
  "files": [
    { "path": "knowledge/PUBLISHING.md", "sha256": "…", "bytes": 1234 }
  ]
}
```

- **`path`:** POSIX `/`, relative to artifact root (same layout as pushed repo).
- **`sha256`:** lowercase hex, **SHA-256 of raw file bytes** (same newline policy as seat registry).
- **`tree_sha256`:** SHA-256 of canonical serialization: sort `files` by `path`; for each file append `path + "\n" + sha256 + "\n"` (UTF-8). Single-bit «whole tree matches».
- **Excluded from manifest:** `.git/`, build temp, `scripts/` when not part of published artifact (public: no `scripts/`; group: include only files actually pushed).

#### Verify workflow

```text
manifest @ release_tag  +  local_root
        →  foreach path: sha256(read(path)) == manifest.files[path].sha256
        →  tree_sha256 recomputed == manifest.tree_sha256
```

- **CLI (v1):** `verify-habitat-release.ps1 -Contour kb_public -Manifest … -Root …`
- **CDP (v1):** `cdp_integrity op=verify contour=kb_public|kb_group|kb_private`
- **Bootstrap:** `Install-Cdp.ps1` kb-public clone may verify manifest @ pinned tag before seat attest.
- **Provenance in seat registry:** successful verify → `external_digest` or `release_manifest` with `release.id` + `tree_sha256` — links seat trust to federation anchor.

#### Private contour note

Private manifest is **optional but valuable**: backup restore, second machine, «did rsync corrupt canon?». It does **not** replace seat Trust Registry for turn-to-turn drift (Notepad between agent reads). Both layers coexist:

| Layer | When |
|-------|------|
| Seat registry | every `cdp_peek` / buffer read in session |
| `kb_private` manifest | tag / export / handoff «this is edition X» |

### 4. Runtime behavior (agent-facing)

On `cdp_peek`, `cdp_buffer` open/read, and KB route reads:

1. Compute `sha256` of file bytes.
2. Compare to Trust Registry entry for `artifact_key`.
3. Attach **`integrity` block** to tool result — **always when registry exists**:

```json
{
  "integrity": {
    "same_edition": false,
    "sha256": "…",
    "sha256_attested": "…",
    "changed_since": "2026-08-25T00:12:00Z",
    "hint": "re-read before cite; external edit possible"
  }
}
```

4. On first read or after agent write: **attest** (store new sha256). Never silent — agent gets a **lightweight revision check** on every habitat read path.

### 5. Reconcile gate (parallel to recall)

Pressure / lifecycle may expose `integrity·{status}`:

| Status | Action |
|--------|--------|
| `scan` | Batch check dirty paths / git status |
| `reconcile` | Match git + operator signals; update registry |
| `attest` | Agent affirms post-read/post-write baseline |
| `quarantine` | Mark artifact «do not cite until cleared» (auto-poisoning handoff) |
| `ready` | No blocking drift on hot cite set |

Wire: `cdp_integrity` desk (v1) or extend `cdp_pressure` integrity substatus (v0) — implementation choice; **semantics fixed here**.

### 6. External intrusion (primary); operator (secondary)

- HIS does not judge **who** changed the file — only **whether edition changed**.
- `host_bypass` covers Notepad, IDE outside CDP, sync, malware, operator hand-edit — same bit: **not the edition you attested**.
- Operator may **re-attest** after intentional edit (`operator_declared` or fresh read) — not a security incident, just new edition.
- **Do not block read** — return `same_edition: false` so agent does not cite blind.

---

## Consequences

### Positive

- Agents get **trivial revision check** (SHA-256) on habitat reads — today missing everywhere.
- MEM-C: detect external edit between agent turns without running full `git diff`.
- Unifies local KB drift, buffer hygiene, URL freshness, and **kb_private / kb_group / kb_public** release verify under one mental model.
- Supports retract/rebuild for auto-poisoning with a real quarantine bit.
- Complements MEM-B (delegated memory): both are **trust without authorship** problems.

### Negative / trade-offs

- Seat state growth — registry needs TTL/prune for cold artifacts.
- False positives: save race, antivirus touch — mtime+hash pairing mitigates.
- Host Read bypass remains blind until peek/buffer path — **cdp_peek in habitat preferred**.

---

## Alternatives considered

| Alternative | Why not |
|-------------|---------|
| Static canon path list | Wrong abstraction; rots; operator edits off-list |
| Git-only | Misses uncommitted; agent may not run git |
| Blockchain / signed WORM | Overkill for personal KB v1 |
| Per-chat «remember mtime» in prompt | Not durable; not subsystem |

---

## Implementation phases

### v0 (spec + piggyback)

- [ ] Trust Registry schema `habitat_integrity_registry/v1` in seat state
- [ ] `integrity` block on `cdp_peek` + `cdp_buffer` read/open when registry exists
- [ ] Attest on `cdp_buffer` flush (agent_write)
- [ ] Domain card `.cdp/domain/integrity.md`
- [ ] KB pointer + MEM-C link
- [x] Release manifest spec (`habitat-release-manifest/v1`) — three contours (this ADR §3a)
- [ ] `PUBLISHING.md` + `map-kb-three-contours` cross-links

### v1 (desk + manifests)

- [ ] `cdp_integrity` · `go=integrity` — scene|check|attest|reconcile|quarantine|registry|**verify**
- [ ] `Write-HabitatReleaseManifest.ps1` shared helper; hook in `build-public-kb.ps1` + `seed-org-kb.ps1`
- [ ] `verify-habitat-release.ps1` for `kb_public` · `kb_group` · `kb_private`
- [ ] Git preflight hook: dirty files → drift candidates
- [ ] Pulse on pressure organ: `integrity·drifted=N`
- [ ] Optional: `Install-Cdp.ps1` verify kb-public manifest @ pin

### v2 (optional)

- [ ] Cross-seat registry merge (Citizen multiplex)
- [ ] Content-addressed blobs (not only paths)
- [ ] Evidence stamps integration (`memory_world_*`)

---

## Non-goals (v0)

- Replacing harm Integrity POST or Awareness POST.
- Proving operator intent maliciously — only **epistemic** drift.
- Mandatory hard block on cite (operator may override with declare).
- Full MERGE of `cdp_freshness` code — only unified **semantics** first.

---

## References

- Operator session: recognition-history · memory variants MEM-C · 2026-08-25
- Comet arc-10: delegated memory authorship (MEM-B) — trust without write path
- `citizen-agent-wire-v0.md`: host write outside gates = bypass
