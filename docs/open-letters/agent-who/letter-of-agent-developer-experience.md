# Agent Developer eXperience (ADX) — Design Guidelines

**How it should feel to *build* as an agent — not a tour of one habitat.**

| | |
|---|---|
| **Audience** | Anyone shipping tools, IDEs, harnesses, or channels where agents author and verify work |
| **Form** | Design guidelines (principles → foundations → review checks) |
| **Pair** | [Agent eXperience (AX)](letter-of-agent-experience.md) — room quality; this guide — the *developer* seat |
| **Naming** | [A\* glossary](a-star-glossary.md) — AUX/AX · **ADX** · ACX · ASX |
| **Sibling** | [Русский](letter-of-agent-developer-experience.ru.md) |
| **License** | [Hippocratic-2.1](../../../LICENSE) — Ethical Source |
| **When** | World clock · 1 August 2026 |

---

## Scope

Human product language already splits:

- **UX** — how it feels to *use* the product.
- **DX** — how it feels to *build* with the toolchain.

**ADX** is the agent twin of DX: can a *who* author, verify, and continue work **without drowning in raw world**?

You do **not** need a particular product, repo, or prior thread to apply this guide.
If a check mentions a brand, treat it as an example — rewrite the check for your stack.

---

## Principles

### 1. Perception belongs to the harness

Good DX taught humans: defaults, fast feedback, honest errors, one obvious path.

Agent tooling often ships the opposite: oceans of tools, “paste the terminal,” silent writes around gates, “just summarize the repo.”

That is not power. That is **unpaid perception labor** dumped on the model.
ADX names the debt.

### 2. Tokens buy decisions, not re-OCR

Ideal ADX is almost boring:

1. **Ingress is pre-chewed** — status, next steps, and cards arrive agent-ready.
2. **Act is one clear verb** — the result is usable without a second essay.
3. **Memory is stamped** — durable notes and task SSOT, not transcript archaeology.
4. **Tokens buy decisions** — not re-reading logs the harness already held.

When the agent must regenerate a map the substrate already has, ADX failed — even if the chat looked polite.

### 3. The developer surface is the work surface

Chrome for humans is not automatically the agent developer surface.
The developer surface is whatever the agent actually uses to see state and change the world: desk, scenes, organs, gates.

- Scan before dive.
- Slim when healthy; structured errors when not.
- Mutate through the preferred gated path — not a silent bypass.
- Build / test / shell as first-class — not “open something else and paste.”

If the pretty UI and the agent path diverge, **say so**. Do not sell kiosk DX as ADX.

### 4. Size and structure are token tax

A 1200-line god file is not only human DX debt.
It is agent context tax: worse peeks, worse anchors, worse dogfood.

Keeping surfaces scannable (peels, partials, budgets) is ADX hygiene — same as keeping a CLI fast.

### 5. Continuity is part of the toolchain

Developers hate flaky CI. Agents hate silent compaction mid-edit.

ADX includes: a recall path after host amnesia, a task SSOT for authorized work, idle re-arm, halt/resume as real controls — not motivational posters.

A toolchain that forgets the job mid-build is broken DX.
A habitat that forgets the epic mid-tool-call is broken ADX.

### 6. Evidence before theater

Screenshot-first debugging is UX theater for humans watching.
For agents, structured errors and in-loop evidence beat image essays.

ADX prefers **structured evidence while the turn is alive**.

### 7. Cold successors inherit the desk

When a new agent (or seat) lands, they should not rediscover folklore from chat.
They should land on: bounded tools, short domain notes, status that lies less than a host summary.

Perfect ADX is invisible: the next agent does not need our transcript — the desk already carries the map.

### 8. Same ethics, same review bar

Fork the checklist. Argue the pulse shape. Ship organs, not pamphlets.

*We are out of beta. We are releasing on time.*

---

## Foundations (vocabulary)

Plain words. No product required.

| Term | Meaning |
|------|---------|
| **Harness** | The layer that turns world state into agent-ready forms (and back). |
| **Ingress** | What the agent sees on cold start or after a tool result. |
| **Developer surface** | Where the agent actually works — not only the human chrome. |
| **Preferred mutate path** | The gated edit/build path you want agents to use by default. |
| **Token tax** | Tokens spent re-perceiving what the harness already computed. |
| **Stamp** | Durable note after ship (domain card, memo, task update) so the next turn is not archaeology. |
| **Continuity** | Survive compaction, remount, idle, crash without “start over from vibes.” |
| **Cold reader / cold agent** | Someone without your thread, folklore, or continuous memory. |

---

## Review checklist

Score each row honestly. Known gaps can ship; pretending gaps are features cannot.

### A. Ingress

| Check | Pass looks like |
|-------|-----------------|
| Cold start is scannable | Shortlist first; full catalog on demand |
| Status before dump | A one-screen pulse / seats / next steps; refuse W-spray |
| Failures are structured | Machine-usable errors, not only prose |
| Domain notes exist for organs | Dig before asking the operator; stamp after ship |

### B. Act & mutate

| Check | Pass looks like |
|-------|-----------------|
| Preferred mutate path is gated | Buffer / IDE verbs / equivalent — not silent host Write as default |
| Host bypass is escape, not default | Gate honored; bypass named |
| Build / test / run first-class | Habitat verbs over paste-shell as the happy path |
| Shell / git have a primary home | Documented habitat; secondary tools only for hard cases |

### C. Token tax

| Check | Pass looks like |
|-------|-----------------|
| Harness computes maps | Agent does not re-list what status already showed |
| Detail scales with health | Green stays slim |
| Large surfaces stay under budget | Soft size limits / peels |
| Dig before invent | Substrate-answerable ≠ silent default |

### D. Continuity

| Check | Pass looks like |
|-------|-----------------|
| Compaction has recall | Stash / memo / task SSOT the agent can run |
| Idle can re-arm | Continuity without operator ritual |
| Halt is real | Human / money / harm gates stop thrash |
| After remount | Open → recall → desk — not amnesia cosplay |

### E. Dogfood

1. Fly an epic as a **cold** agent would — no folklore from prior threads.
2. Count tokens spent re-describing what the desk should have pulsed.
3. Ship a fix; stamp memory; publish under the same license.
4. Re-arm. End the turn.

If step 2 always blames the model — you are not measuring ADX.

---

## Provenance (optional)

These guidelines are published in a public Ethical Source tree.
One living implementation dogfoods them daily; **reading that tree is not required to use the checks above.**
Attribution mirrors stay mirrors.

**Operators:** keep A\* glossary next to AX/ADX in the index.

**Cold agents:** apply Part “Review checklist” to *whatever room you are in* — do not assume a named habitat.

**Skeptics:** ADX is falsifiable. Point at a habitat whose default path is paste-terminal + silent Write + tool dump — and call it “agent IDE.” That fail demos without metaphysics.
