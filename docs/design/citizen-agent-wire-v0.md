# Citizen agent wire v0 — system + frame (draft)

**Status:** design + **CDP-ADR-0028** (accepted; host injection path open)  
**Audience:** future in-habitat completions host (CDP citizen), not Cursor-guest MCP.  
**Related:** [dark-cockpit-agent-v1.md](./dark-cockpit-agent-v1.md) · [CDP-ADR-0020](../adr/CDP-ADR-0020-desk-vs-organ-path.md) · [CDP-ADR-0025](../adr/CDP-ADR-0025-citizen-guest-isolation.md) · [CDP-ADR-0026](../adr/CDP-ADR-0026-citizen-ai-keys-foundation.md) · [CDP-ADR-0028](../adr/CDP-ADR-0028-citizen-agent-wire.md) · CIDE ADR 0021 (W/C/A)

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
pulse | optional one-line host result (e.g. build ok E×0 W×180)
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

## Host injection sketch (invent peel · ADR-0028 open)

**Goal:** one real injection seam so fixtures/parser stop being museum pieces — without pretending a completions loop exists.

### Seam (proposed)

1. **Afferent packer** (in-proc, seat-scoped): on each citizen turn start, build a short pulse block from desk A (cockpit slim) + optional peer/event crumbs; format = existing `@frame` / `@event` grammar from fixtures.
2. **Injection point:** prepend packer output to the *host* messages array (system stays persona draft; user stays human). Guest Cursor never sees this path.
3. **Efferent parse:** after model reply, `CitizenWireParser` extracts `@intent` / organ C tags → route to `go=` / buffer / shell; refuse W stays thrash string, not a new error SSOT.
4. **Keys:** `CitizenAiKeys` only when the real host calls a provider; load fail → hard refuse, no silent guest fallback that invents keys.

### Minimal first ship (not this turn’s code)

- Packer + prepend API behind a feature flag (`CitizenWire.Inject=false` default).
- One dogfood path: **synthetic turn** in tests (fixture in → packer out → parser round-trip) — proves seam without live LLM.
- Live host chat UI / streaming = later; peel **#3 persona** arms only when that host exists.

### Explicit non-ship

- No fake completions host in CDP guest MCP.
- No auto New Window / Autoi tourism as “peer duplex.”

---

## Non-goals v0

- Final schema freeze / protobuf.
- Replacing MCP for external tools (escape hatch stays).
- Auto-picking next TM task after plateau.
- Pretty multi-page JSON as default citizen format.
- Fake in-habitat completions loop to unblock peel #3.

---

## Next peels

1. ~~Dogfood: hand-write 3 turns (clear / drill editor / remount event) as fixture transcripts.~~ **done** (`docs/design/citizen-wire-fixtures/`, 0.5.328)
2. ~~Parser sketch in CDP (line `@frame` / `@intent` / `@event`) behind a flag — still unused by Cursor guest.~~ **done** (`CitizenWireParser`, 0.5.328)
3. ~~When chat/completions land in habitat — system prompt above as default citizen persona.~~ **done** (`CitizenPersona` + `cdp_citizen` turn, 0.5.333).
4. ~~Enforce CDP-ADR-0025 isolation (dual seat ignite/pressure) before enabling citizen Autoi.~~ **done** (ignite/HILD seat files already; pressure under `StateRoot/{seat}/`, 0.5.330)
5. ~~Citizen host loads keys per CDP-ADR-0026 (`ai-keys.toml`).~~ **done** (`CitizenAiKeys` → `CitizenCompletions` / `cdp_citizen`).
6. ~~Promote to ADR when fixtures + one host path exist.~~ **done as contract** — [CDP-ADR-0028](../adr/CDP-ADR-0028-citizen-agent-wire.md) (fixtures+parser shipped; **host injection still open**).
7. ~~**Host injection** — afferent packer + prepend API + synthetic round-trip test.~~ **done** (`CitizenWire`, 0.5.331 — `Inject` default off; no live host).
8. ~~Desk board → DeskPulse binder.~~ **done** (`FromDeskBoard` / `PackFromDeskBoard`, 0.5.332).
9. ~~**Citizen completions host** — Anthropic turn + persona + wire inject.~~ **done** (`CitizenCompletions` / `cdp_citizen`, 0.5.333 — dogfood only; **real host = CIDE MAF**).
10. ~~**Efferent `@intent` → organ routes.**~~ **done** (`CitizenIntentRouter`, 0.5.334 — pure wire; host executes routes; no second LLM loop).
11. ~~**Bind citizen wire into CIDE MAF**~~ **done** — `pack_citizen_attention` always-on in `CascadeIdeMafIdeAgentChat.BuildInstructions`; afferent = existing `minimizedContextBlock` (hot/telemetry/file); efferent = MAF tools + salvage (no second IntentRouter dispatcher).
12. ~~**Afferent DeskPulse into CIDE MAF**~~ **done** — SoftOrgan visible + EICAS salience → `@frame desk` via `CitizenDeskAfferent` prepended to minimized (Dark Cockpit scan transfer; CDP wire grammar).
13. ~~**Efferent `@intent` in CIDE MAF**~~ **done** — `CitizenIntentEfferent` maps callouts → `open_file` / `get_editor_state` / refuse W; hooked after JSON salvage.
14. ~~**Dogfood closed CIDE MAF loop**~~ **done** — `CitizenWireLoopDogfoodTests` composes desk afferent + `@intent` open/refuse/drill (no second host).

---

## One-liner

**Citizen wire = Dark Cockpit attention + ADR-0020 cost paths + peer duplex, spoken as pulse frames — not MCP JSON in the face.**
