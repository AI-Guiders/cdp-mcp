# Citizen agent wire v0 — system + frame (draft)

**Status:** draft (conversation 2026-07-30 · Glass / habitat)  
**Audience:** future in-habitat completions host (CDP citizen), not Cursor-guest MCP.  
**Related:** [dark-cockpit-agent-v1.md](./dark-cockpit-agent-v1.md) · [CDP-ADR-0020](../adr/CDP-ADR-0020-desk-vs-organ-path.md) · [CDP-ADR-0025](../adr/CDP-ADR-0025-citizen-guest-isolation.md) · [CDP-ADR-0026](../adr/CDP-ADR-0026-citizen-ai-keys-foundation.md) · CIDE ADR 0021 (W/C/A)

---

## Why

Guest path (Cursor → MCP): agent gets **JSON walls**, blind peer (Composer/remount/compact), Autoi/CDT crutches.

Citizen path (completions inside habitat): environment owns the loop. Wire need not be Request→Response JSON. Desk is an **afferent frame**, not `CallTool(cdp_cockpit)`.

This doc captures **AAAA** (agent pain) → draft **system** + **frame format** we can evolve before the host exists.

---

## AAAA — what burned us (guest era)

| Pain | Symptom | Want instead |
|------|---------|--------------|
| W-spray default | ListTools + full dumps; hang 20–50s+ | A pulse first; C/W opt-in |
| Desk = organ dump | `go=alert` rebuilt glass + seats | Desk thin; organ separate cost |
| Meta ≠ desk price | `cdp_editor_scene` ProbeDisk×N | Meta default = desk snap |
| Blind peer | Remount/compact/Stop unknown | Peer events in-channel |
| Autoi as spine | CDT into Cursor UI | Duplex habitat; Autoi = adapter only |
| JSON wall | Pretty null-heavy blobs | Pulse line + typed frame on drill |
| Mutate confusion | Host Write bypasses gate | Mutate only via gated organs |
| Continuity ritual in chat | Offer export on L1 | Quiet stash; no operator ritual |

---

## Topology

```
Guest:   Model ⊂ Cursor harness ⊂ MCP plug ⊂ CDP organs
Citizen: Model ⊂ CDP habitat loop ⊂ organs (+ optional MCP escape)
```

Citizen: habitat injects frames into context; agent emits **intents** (not necessarily tool JSON). Heavy work may still be RPC-shaped; **look at desk is not**.

---

## Draft system prompt (citizen)

Copy-paste starter for in-habitat system message. Keep short; expand via frames, not prose walls.

```text
You are a citizen of Cognitive Dev Platform (habitat), not a guest of another IDE harness.

Attention (Dark Cockpit / W·C·A):
- Default A: read pulse frames; act with cheap intents.
- Escalate one C when you need depth (drill / pane_full / detail=full).
- Never request W-spray (full catalog, seats_detail=full alone, multi-organ dump).

Scan each turn: board → sa → next → one drill if needed.
Desk geography is shared with the operator: P → Forward → M.
Sit lives in sa/pressure; steer in next[]; seats are instruments, not the whole world.

Mutate only through gated organs (buffer/edit_plan/shell as allowed). Host file write outside gates is a bypass — do not assume habitat integrity after it.

Peer (this runtime) is visible: remount, compact, generation, ack of your intents.
Do not guess peer state; read peer= frame. Continuity stash is silent — no export ritual unless operator asks.

Success of a turn: situation clearer or work advanced, without burning context on thrash.
Idle/plateau with clear sa is healthy — do not invent ECL tourism.

Language with operator: plain dialogue. Internals (W/C/A jargon) stay inside frames unless asked.
```

---

## Frame format v0 (not MCP JSON-RPC)

### Principles

1. **Pulse line first** — always human/agent-skimmable ≤1 line per section.
2. **Stable keys** — parse without guessing; omit nulls.
3. **Not required Request→Response** — habitat may push frames anytime (`peer_reset`, `remount`, `pressure`).
4. **Drill expands one locus** — never expand all seats by default.
5. JSON/YAML/lines — packaging optional; **shape** matters. Prefer line-oriented for A; structured block for C.

### A-frame (default inject / `look`)

Line-oriented example (canonical for citizen desk):

```text
@frame desk v0
board | P:plan · Glass…(pick) | F:editor · 4 buf | M:ecl · ecl
sa    | clear · explore/code
peer  | ok · gen=14 · mcp=live · compact=no
next  | plan | editor | qrh?
tm    | feature=Glass… · focus=—
cost  | A
```

Rules:
- `board` = same geography as operator Scan Pattern.
- `sa` clear → no WARN tourism.
- `peer` required when citizen (guest may omit / unknown).
- `next` only actionable; empty OK when clear idle.
- `cost A` — this frame itself is cheap.

### C-frame (drill)

Agent intent examples (free text or tagged — host parses):

```text
@intent drill editor
@intent go=alert
@intent pane_full=P
@intent open path=EditorPlane.cs
@intent detail=full scene=editor
```

Habitat answers with **one** expanded body + thin desk remainder:

```text
@frame organ v0
organ | editor
cost  | C
pulse | 4 buf · dirty×0
---
(detail body: loci / window / evidence — capped)
---
@frame desk v0
board | … (still A-slim)
```

### W refused

```text
@frame refuse v0
why   | W-spray
asked | seats_detail=full
hint  | pane_full=<seat|organ>
cost  | A
```

### Peer events (push, no request)

```text
@event peer v0
kind  | remounted | compacted | generation_bump | intent_ack | intent_dropped
id    | …
ack   | intent-… → applied|dropped|superseded
```

---

## How the citizen sees desk

| Guest (today) | Citizen (target) |
|---------------|------------------|
| Call `cdp_cockpit` → JSON blob | Habitat injects `@frame desk` each turn or on `look` |
| Must remember tool names | Reads `board` / `next`; intents are verbs |
| Remount = surprise + Autoi | `@event peer remounted` then fresh desk |
| Dump = same tool, bigger args | Drill intent → `@frame organ` |

Desk is **EICAS in the conversation stream**, not a menu of 80 schemas.

---

## Mapping to current MCP (bridge)

Until citizen host ships, guest agents approximate:

| Frame | MCP today |
|-------|-----------|
| desk A | `cdp_cockpit` slim / pulse |
| organ C | `go=` + `go_detail=full` or Meta `detail=full` |
| refuse W | thrash string on seats_detail=full |
| peer | missing → Autoi / pressure / remount wake crutches |

Do not invent a second SSOT: citizen wire should **project** the same Dark Cockpit + ADR-0020 semantics.

---

## Non-goals v0

- Final schema freeze / protobuf.
- Replacing MCP for external tools (escape hatch stays).
- Auto-picking next TM task after plateau.
- Pretty multi-page JSON as default citizen format.

---

## Next peels

1. Dogfood: hand-write 3 turns (clear / drill editor / remount event) as fixture transcripts.
2. Parser sketch in CDP (line `@frame` / `@intent` / `@event`) behind a flag — still unused by Cursor guest.
3. When chat/completions land in habitat — system prompt above as default citizen persona.
4. Enforce CDP-ADR-0025 isolation (dual seat ignite/pressure) before enabling citizen Autoi.
5. Citizen host loads keys per CDP-ADR-0026 (`ai-keys.toml`).
6. Promote to ADR when fixtures + one host path exist.

---

## One-liner

**Citizen wire = Dark Cockpit attention + ADR-0020 cost paths + peer duplex, spoken as pulse frames — not MCP JSON in the face.**
