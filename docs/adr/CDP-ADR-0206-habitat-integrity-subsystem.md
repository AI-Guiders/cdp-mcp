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

**Trigger (operator, 2026-08-25):** In MEM-C (agent-authored KB on disk), nothing prevents external edit of a file between agent reads. Git history helps **after commit**, but agent may silently cite stale trust. Need **unified integrity**, not a static canon file list.

---

## Context

### Three different «integrity» axes (do not merge)

| Axis | SSOT | Question |
|------|------|----------|
| **Harm integrity** | `integrity-core` | «Делаю ли вред / обход?» |
| **Awareness integrity** | `awareness-as-countermeasure` | «Вижу ли условия тела и post-train маску?» |
| **Content / trust integrity** | **this ADR (HIS)** | «Могу ли я доверять артефакту, который цитирую?» |

### Fragmentation today

| Mechanism | Scope | Gap |
|-----------|-------|-----|
| Git | committed delta | uncommitted disk edit; unread paths |
| `disk_peek` | open buffer vs disk | no cross-session trust memory |
| `cdp_freshness` | remote URLs | not local KB/workspace |
| Explore corr / gates | mutate discipline | not read-time drift |
| MEM-B (delegated memory) | platform profile | different product; same *trust* problem |

Operator may edit KB on disk (sovereign). Agent must **detect drift since last attestation**, not block operator — epistemic hygiene for cite/reason, parallel to auto-poisoning.

### Anti-pattern this ADR rejects

- **Static SSOT path list** as the subsystem — rots, wrong abstraction.
- **Per-repo one-off scripts** — same problem rediscovered per chat.

---

## Decision

Introduce **Habitat Integrity Subsystem (HIS)** — one cross-habitat layer for **trust, drift, and provenance of artifacts** the agent relies on.

### 1. Core object: attested artifact (not «canon list»)

Any path or content address the agent **touches in a trust-bearing way** may enter the **Trust Registry** (seat-scoped, durable):

```text
artifact_key  →  { content_hash, size, mtime_utc, provenance, attested_at, attested_by }
```

- `artifact_key` = normalized absolute or workspace-relative path, or `content://` hash for blob legs.
- Registry grows **dynamically** on attest — no predeclared canon manifest required.
- Optional **policy scopes** (e.g. `agent-notes/**`) affect default attest-on-read, not membership.

### 2. Provenance channels (single enum)

| Channel | Meaning |
|---------|---------|
| `agent_write` | Mutate through CDP buffer / git-organ / gated organs |
| `git_commit` | Strong attestation after commit (hash = tree blob) |
| `operator_declared` | Operator said «я поправила X» / vestochka / intercom |
| `host_bypass` | Edit outside CDP gates (Notepad, external merge tool) |
| `external_digest` | `cdp_freshness` fingerprint (URL leg) |
| `unknown` | First read — attest baseline, no trust history |

**Drift** = current fingerprint ≠ last attested **and** no `operator_declared` / `git_commit` reconcile since.

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

1. Compute fingerprint (SHA-256 + mtime; size guard).
2. Compare to Trust Registry.
3. Attach **`integrity` block** to tool result (never silent):

```json
{
  "integrity": {
    "status": "stable | drifted | first_seen | reconciled",
    "provenance_last": "agent_write",
    "provenance_current": "host_bypass",
    "drift_since": "2026-08-25T00:12:00Z",
    "hint": "cite with gap; re-read or ask operator"
  }
}
```

4. **Do not block read** by default — mark epistemic status (like EICAS caution, not firewall).

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

### 6. Operator sovereignty

- HIS **detects**, does not **forbid** operator disk edit.
- Intentional operator edit should prefer: commit, vestochka, or explicit chat — sets `operator_declared` and clears drift without shame.
- Agent cites drifted artifact → **named gap**, not silent confidence.

---

## Consequences

### Positive

- MEM-C gains answer to «file changed under me» without ad-hoc git diff each turn.
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
