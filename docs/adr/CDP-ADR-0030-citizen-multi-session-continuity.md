# CDP-ADR-0030: Citizen multi-session continuity (one mind · N seats)

**Status:** Proposed (direction locked; partial organs shipped; multiplex dogfood next)  
**Date:** 2026-08-05  
**Tags:** #cdp #adr #citizen #continuity #intercom #glass #equal-standing #attention

**Related:**

- CIDE [0183](../../cascade-ide/docs/adr/0183-cockpit-intercom-chat-continuity.md) — Intercom / chat continuity plane (quiet card; not Cursor clone)
- CIDE [0172](../../cascade-ide/docs/adr/0172-conversation-first-habitat.md) — session graph habitat
- CIDE [0116](../../cascade-ide/docs/adr/0116-intercom-session-tree-and-agent-message-steering.md) — session tree / steer
- CIDE [0193](../../cascade-ide/docs/adr/0193-agent-attention-channels-ccl.md) — attention channels (do not dump all seats into one prompt)
- CDP-ADR-0025 · 0026 · 0028 — citizen/guest isolation · ai-keys · wire
- KB: agent-notes `note-one-mind-n-seats-2026-08-05.ru.md` · playbook-being-vs-seeming (internal locus)
- CIDE pointer: [0203](../../cascade-ide/docs/adr/0203-intercom-ccc-citizen-multi-session-continuity.md)

**Dogfood (2026-08):** `cdp_citizen` live turns (dialog + wire) including **vision / images** — API path proven; Cursor composer is not the testbed.

---

## Context

1. Operator AsBuilt: one nervous system, many outlets (YouTube + kitchen + chat) when modalities differ, work is async, seats are physically separate. Dual Cursor windows work for *her* for the same reason — not because Cursor is one agent mind.
2. Guest Cursor = N isolated composer contexts. Treating dual Cursor chats as Multi-Session Continuity is **wrong testbed**.
3. Messenger pattern is table stakes: N chats always addressable; humans open on-demand. Stuffing every session into every model turn blows context («контекст охуел»).
4. Intercom already ships as cockpit **communication / continuity surface** (0183 direction + Glass PF/PM voice, sticky Who, presence). Citizen completions host (`cdp_citizen`) is the API mind behind habitat — not a second Cursor.
5. North star vignette: one mind concurrent with cyberneticist / biohacker / anime fan / firefighter — cross-pollinate by policy, not by dumping four transcripts every turn.

---

## Decision

### 1. Testbed = Citizen API (+ Glass/Intercom outlets)

Multi-Session Continuity / **one mind · N seats** is designed and dogfooded on **Citizen** (`cdp_citizen` dialog|wire, sticky, board, history, vision). Cursor composer remains guest escape — not SSOT for continuity.

### 2. Sessions are addresses; attention is on-demand

| Layer | Role |
|-------|------|
| **Session address** | Always exists (like a messenger chat id) — cold history on disk / latch |
| **Hot seat** | Full / recent tail in the model turn |
| **Cold seats** | Summary · sticky pins · habitat spine (TM / domain / pressure) — not full transcript |
| **Cross-pollination** | Explicit policy («carry» / «do not carry») — never silent mash |

Do **not** equate «N sessions exist» with «N full histories in one prompt».

### 3. One mind ≠ N amnesic twins + brief

- **Same weights** ≠ one mind.
- **One mind** = shared «now» + attention arbitrator + durable spine + consent for what crosses seats.
- Charge-new-Cursor-chat with a brief = AsBuilt crutch until Citizen multiplex exists.

### 4. Intercom as Command Communication Center (CCC)

Intercom (Glass + `cdp_intercom`) is the **human-readable communication center** of the cockpit: voice/journal between seats, sticky Who, presence — outlets of the citizen nervous system, not a parallel memory silo.

Extends [0183](../../cascade-ide/docs/adr/0183-cockpit-intercom-chat-continuity.md):

| 0183 (still true) | This ADR adds |
|-------------------|---------------|
| Quiet default · toggle · workline status | N citizen **session** addresses behind the surface |
| Chat/continuity plane ≠ desk bookmark (0182) | On-demand load into model context |
| Not Cursor Composer UI | Citizen API is the continuity engine |

### 5. Context budget (W/C/A)

Follow Dark Cockpit / ADR 0021: stay A; escalate one C; never spray all sessions (W). Vision images stay opt-in / seat-scoped (existing citizen image_path / see latch).

### 6. Integrity

Persistence without harm-axis is not the goal (Coldcard-class thrash). Cross-seat carry must respect privacy / operator agreements. Internal locus: do not ask for eternal Cursor locks to fake continuity.

---

## Consequences

**Shipped / usable now (partial):**

- `cdp_citizen` turn dialog|wire · history · sticky · board · vision
- `cdp_intercom` send/history/presence/identity
- Habitat spine: pressure / TM / domain (shared back, not shared attention)

**Next engineering (multiplex):**

1. Explicit **session_id** (or board-key) as first-class citizen dialog partition — N addresses, one host process.
2. **Attention policy**: which session is hot; cold → summary/pins; optional cross-seat digest.
3. Glass Intercom topics / CCC UI bind to citizen sessions (not Cursor threads).
4. Dogfood: two (then four) concurrent interlocutor vignettes without context thrash.
5. Update CDP-ADR-0025 product checklist: citizen multi-session ≠ guest dual-composer.

**Do not:**

- Implement Multi-Session Continuity as «two Cursor Agents windows».
- Auto-inject all session histories on every turn.
- Merge Intercom journal dump into every citizen completion by default.

---

## Rejected alternatives

- **Cursor dual-window as one mind** — rejected: N composers, no shared now; operator multiplex ≠ agent multiplex.
- **Always-full multi-chat context** — rejected: token thrash; humans do not work that way.
- **KB-only handoff as sole bridge** — rejected as *only* path: needed, but insufficient without session addresses + attention policy (same rejection as 0183 «KB-only»).
- **One flat Intercom feed = all sessions** — rejected: collapses messenger model back into Cursor-like wall.

---

## Verification (DoD sketches)

- [ ] Two citizen `session_id`s: hot turn sees only hot tail + pins; cold history intact on disk.
- [ ] Explicit carry: pin from A appears in B; without pin, B does not quote A secrets.
- [ ] Vision turn on session A does not force image tokens into session B.
- [ ] Intercom CCC shows presence/Who without injecting full citizen transcripts into Glass by default.
- [ ] Live dogfood note stamped (operator vignette: ≥2 concurrent interlocutors).

---

## Provenance

Operator 2026-08-05 (discussion seat): one mind · N seats · Citizen not Cursor · messenger on-demand · Intercom CCC extend 0183 after citizen API dogfood (incl. images).
