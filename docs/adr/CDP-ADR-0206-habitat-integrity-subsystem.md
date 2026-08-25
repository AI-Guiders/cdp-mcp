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
| `unknown` | first read — baseline attest only |

### 3. Subsystem legs (one engine, many surfaces)

```text
                    ┌─────────────────────────┐
                    │   Habitat Integrity     │
                    │   (Trust Registry +     │
                    │    Drift Engine)        │
                    └───────────┬─────────────┘
          ┌─────────────────────┼─────────────────────┐
          ▼                     ▼                     ▼
   local_kb / workspace   buffer↔disk          external URLs
   (peek/buffer/git)      (disk_peek)          (cdp_freshness)
```

- **`cdp_freshness` is not a separate world** — it feeds `external_digest` provenance into the same registry semantics.
- **Git** is reconcile source of truth when available, not a substitute for seat trust memory between reads.

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
- Unifies local KB drift, buffer hygiene, and URL freshness under one mental model.
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

### v1 (desk)

- [ ] `cdp_integrity` · `go=integrity` — scene|check|attest|reconcile|quarantine|registry
- [ ] Git preflight hook: dirty files → drift candidates
- [ ] Pulse on pressure organ: `integrity·drifted=N`

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
