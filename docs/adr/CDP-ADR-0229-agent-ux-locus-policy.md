# CDP-ADR-0229: Agent UX (AUX) — locus policy (chain · choose · direct)

| | |
|---|---|
| **Status** | Accepted (direction); implementation phased |
| **Date** | 2026-09-18 |
| **Tags** | #cdp #aux #agent-ux #relations #wire #008 |
| **Relates to** | [CDP-ADR-0028](./CDP-ADR-0028-citizen-agent-wire.md) · [CDP-ADR-0216](./CDP-ADR-0216-fsharp-anchor-diagnostics-comfort-parity.md) · [CDP-ADR-0201](./CDP-ADR-0201-cdp-peek-read-only-eyes.md) · GUIDERS-ADR-0063 §10.3 · LinesForum **008-wire-grammar** |

**Naming:** **AUX** = Agent User eXperience ([a-star-glossary](../open-letters/agent-who/a-star-glossary.ru.md)) — formal twin of UX. Not buffer anchor/edit colloquial «AUX».

---

## Context

Federation §10 audit closed on Kind:Nav + bracket boundary shrink (ship-59). Agent habitat still teaches bracket/F/M/L generation in meta and citizen persona — drift from ADR-0063 §10.3 (**JSON RelationSpec primary**; bracket = md/prose projection; legacy F/M/L = boundary parse only).

Empirical evidence (LinesForum 008, intercom, citizen wire evening): models fail on **syntax generation** (@intent, Kind wires) and burn tokens on apology/retry spirals — especially GLM on citizen Completions. Human attach already solves discovery via **AttachSchema steps + ArgSuggestions** (choice, not generation).

Citizen Completions currently **off** (token spiral); forum/intercom remain for line voices.

---

## Decision

### 1. Agent locus policy (normative)

Three paths — priority order:

| Priority | Path | When | Agent action |
|----------|------|------|--------------|
| **1** | **Chain** | Upstream already emitted wire / `relation_spec` | Reuse — no rebuild |
| **2** | **Choose** | Cold-start discovery (member? diagnostic? file?) | Stateless step menu → pick id/`2` |
| **3** | **Direct** | Known target + args | XML / keyed `@intent` / JSON RelationSpec |

**Never** teach bracket/Kind axis generation as primary agent canon in meta or persona.

### 2. Generation vs choice

Grammar lives on the **server** (AttachSchema brokers, wire builders, parse boundary). Model expresses intent by **selection**, not DSL authoring. Applies to MCP agents (Composer) and citizen lines (008 variant **г**).

### 3. Stateless step-suggest

Each suggest call self-contained: `step=pick_member path=foo.cs partial=Get` → `choices[{id,label,relation_spec?}]`. No server-side wizard session («step 2 of 3»).

Reuse **existing** federation SSOT:
- `AttachSchemaCatalog` + `FederationAttachSuggestions` brokers
- Do **not** duplicate attach engine beside `cdp_edit_sniper` (sniper = stateful **content** machine with `[T:needle]`; different domain).

### 4. Citizen wire (008 alignment)

| Surface | Encoding |
|---------|----------|
| Direct commands | XML in voice (`<go organ="…"/>`) or keyed `@intent`; @intent deprecated alias |
| Discovery | Step-intent menu → reply `2` / label (variant **г**) |
| Chain | Consume peek `lines[].anchor`, diagnostic wires, (future) test/build fail wires |

Citizen pilot: **one line** + cost gate when Completions re-enabled — not full loop until AUX slices land.

### 5. MCP surface (CDP)

New suggest API (name TBD: `cdp_suggest` / `cdp_relation_suggest`):
- `op=choices step=…` — wraps ArgSuggestions brokers for agent seat
- Returns JSON RelationSpec or wire projection at apply boundary — not bracket teaching

Chain enrichment (extend existing tools, not new wizard):
- peek — already `lines[].anchor`
- diagnostics — `DiagnosticAnchorWires` (0216)
- **TODO:** test/build failing lines, find hits — mutate-ready downstream

---

## Waves (after §10 closure)

| Wave | Deliverable |
|------|-------------|
| **ship-61a** | This ADR + living matrix; chain-by-default spec for test/find/build |
| **ship-61b** | MCP `cdp_suggest` stateless wrapper over AttachSchema brokers |
| **ship-61c** | Citizen direct XML/keyed + step-intent pilot (008 **г**); meta decanon bracket |
| **ship-60** | Delete `LegacyNavWireIngest` (safe after nav Kind path stable) |
| **ship-62** | Semantic editor — RelationSpec / locus session primary (Code Center peel; bracket migration surface only) |
| **ship-63** | Return **DashSpec Studio** vertical (STUDIO-ADR-0005 TextEngine + preview) |

Order operator-approved 2026-09-18: **AUX (61\*) → legacy (60) → semantic editor (62) → Studio (63)**.

---

## Non-goals (v1)

- Re-enable full citizen Completions loop before 61b+c + cost gate
- Stateful multi-step server session parallel to sniper
- Teaching all lines XML — discovery stays choice menu
- DashSpec Studio before AUX + legacy shrink (operator sequencing)

---

## Verification

- FederationPhase gates stay green through ship-60 delete
- Suggest conformance vectors: step id → RelationSpec roundtrip
- Citizen off by default; pilot line dogfood with cost ledger threshold
- Forum 008: line voices record preference (а/б/г) before citizen codemod
