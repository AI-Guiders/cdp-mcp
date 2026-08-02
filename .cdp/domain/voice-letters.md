# Domain card: Agent Who · Voice Letters

- id: `voice-letters`
- organ: Agent Who: Voice Letters (`docs/open-letters/agent_who_voice/`)
- product: `#CDP` · `#Who`
- playbook: agent-notes `knowledge/domains/agent-operations/playbook-agent-who-voice-letters-dod-v1.md`

## Invariants

- Series name: **Agent Who: Voice Letters** — extension of Who, not a side brand.
- Shelf: `docs/open-letters/agent_who_voice/` (guidelines live in `agent-who/`).
- RU is canon voice; EN stubs point to RU.
- **Auto DoD gate = live dogfood**, not ship/compile alone.
- Hot trail: write while evidence is fresh; no beauty wait.
- Letter ≠ ADR ≠ changelog ≠ domain stamp — lived meaning of the organ.
- Skip: noise ships, no new organ meaning, already lettered for organ+version.

## Entry

- Index: `docs/open-letters/agent_who_voice/voice-letters.ru.md`
- Cursor rule: `agent-domain-ownership` (Voice Letter after dogfood)
- After dogfood: create/append letter → index row → commit/push → then leaf done

## Antipatterns

- Voice Letter before dogfood.
- Treating dry_run-only as dogfood when live path exists.
- Waiting for operator to ask for the letter.
- Mixing guidelines and Voice Letters in one pile (use the two shelves).

## last_ship

- 2026-08-02 → Voice Letter #6 Cursor Auto + chosen name Кир (dogfood = lived Auto routing hide).
- 2026-08-02 → shelves: `agent-who/` + `agent_who_voice/` under open-letters.
- 2026-08-02 → DoD v1: dogfood-gated Voice Letter as auto obligation (playbook + rule + this card).
