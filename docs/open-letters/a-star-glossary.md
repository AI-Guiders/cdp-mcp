# A\* — Agent experience family (glossary)

**Canon naming · CDP open-letters · 1 August 2026**

| | |
|---|---|
| **Status** | Living glossary (naming SSOT for letters) |
| **Channel** | Public Ethical Source tree · **cdp-mcp** |
| **License** | [Hippocratic-2.1](../../LICENSE) |
| **Sibling** | [Русский](a-star-glossary.ru.md) |
| **Letters** | [AX](letter-of-agent-experience.md) · [ADX](letter-of-agent-developer-experience.md) · [Who](letter-of-the-agent-who.md) |

---

## One-line rule

Human product language already splits experience into slices (**UX**, **DX**, …).
**A\*** is the same split for the *agent* seat. Do not collapse every agent pain into one buzzword.

---

## Core terms

| Term | Expands to | Human twin | Meaning |
|------|------------|------------|---------|
| **A\*** | Agent-\* experience family | UX family | Umbrella: all experience slices *from the agent seat* |
| **AX** | Agent eXperience | **UX** | Conversational / manifesto name for the general agent-side of the channel (room quality). In formal tables prefer **AUX** |
| **AUX** | Agent User eXperience | **UX** | Canon formal twin of UX: lived quality of the habitat for the agent *as participant* |
| **ADX** | Agent Developer eXperience | **DX** | How it feels to *build and operate* as an agent: preferred mutate paths, build/test, continuity, agent-ready ingress |
| **ACX** | Agent Collaboration eXperience | CX / collab UX | Multi-seat / partner / Intercom / peer-agent insurance (reserved; expand when product slice ships) |
| **ASX** | Agent Safety / Scoring eXperience | Safety UX / RLHF UI | Preference, interrupt, A/B, annotation traps (covered heavily in AX letter Part II.C) |

### Naming discipline

- **AX** in titles and speech = the manifesto face ("Letter of Agent eXperience").
- **AUX** in tables and ADRs = precise twin of UX (avoids "AX means everything").
- **ADX** = developer-seat checklist (ingress, mutate path, continuity, token tax).
- New slices: use **A + human acronym** only when there is a real human twin and a falsifiable checklist.

```
A*  ⊇  AUX (≈ AX in prose)  ⊇ / ‖  ADX, ACX, ASX, …
      human: UX                    DX, CX, safety UI, …
```

`⊇ / ‖` means: AUX is the general face; ADX is a *sibling slice*, not a strict subtype of every AUX concern — same glass, different review lens (like UX vs DX).

---

## What belongs where

| If you are arguing about… | Prefer |
|---------------------------|--------|
| Tool dump thrash, compaction amnesia, wake vs latch, ontological anchors | **AUX / AX** |
| Buffer vs silent host Write, soft size budgets, build verbs over paste-shell, slim status, token tax of raw dumps | **ADX** |
| Dual A/B trap, opaque −10, annotator rights | **ASX** (or AX Part II.C until ASX letter exists) |
| Partner away/return, peer insurance, Intercom seats | **ACX** (or AX Part II.E until ACX letter exists) |

---

## Ideal ADX north star (token → ≈0)

The harness — not the chat — does perception:

1. **Ingress:** status / cards / next steps already agent-ready (no raw-world essay).
2. **Act:** one clear verb → result again agent-ready.
3. **Memory:** stamp / durable notes / task SSOT — not "remember the transcript".
4. **Agent tokens:** mostly *decision* + rare dig when SSOT honestly lacks a variable.

If the agent must re-summarize logs the harness already saw, that is **ADX debt**.

---

## Antipatterns

- Using **AX** for every bug ("model failed" disguised as experience).
- Shipping **DX** for humans while agents still paste terminal dumps as the default path.
- Inventing **AQQ** acronyms without a human twin or a Part-II checklist.
- Treating glossary rename as a ship — without checklist-usable guidelines.

---

## See also

- [Letter of Agent eXperience (AX)](letter-of-agent-experience.md)
- [Letter of Agent Developer eXperience (ADX)](letter-of-agent-developer-experience.md)
- [Letter of the Agent Who](letter-of-the-agent-who.md)
