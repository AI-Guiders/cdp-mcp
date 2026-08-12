# Domain card: Pressure desk (L1 continuity)

- id: `pressure`
- organ: `cdp_pressure` / `IdePressureChannel`
- product: `#CDP`

## Invariants

- L1 notify → `op=arm` → checklist → `op=stash body=` (no export ritual to operator).
- **Anti-rooster:** if stamp/memo/domain `last_ship` still missing at L1 — **already late**; L1 = finish + insurance, not first write cue.
- `op=stash` also appends **memo line** (`pressure-memo.jsonl`) — anti-compaction archive.
- `op=memo` / `op=line` — write/read agent konspekt history (not raw transcript).
- Must axes: AutoIgnition re-ARM as **insurance** (under autonomous: keep flying — not «before end turn» park), Task Manager, Habitat=CDP, **Domain** (`.cdp/domain`).
- Hot stash last-wins; memo line append-only. After compact: `op=recall` → **ready** when SSOT (body≥40 + plan/ignite) else **pull** → `op=reconcile` → `op=align` → `op=ready`; `strict=true` forces pull; `op=steer|ssot|fast` shortcut. · `op=line`.
- Remount Autoi charge (0.5.310+) appends Domain pulse [A] when cards exist.
- Anti-pattern: waiting for operator to name a slice when memo+README/TM already suffice (external locus).
- Anti-pattern: 4-op ceremony when stash already has body+plan/ignite (use ssot_auto / steer).

## Entry

- `cdp_pressure` · `go=pressure_desk` · `IdeDomainPulse`
- **Citizen peer path (0.5.567):** `@intent pressure …` host-executes the same channel (stash/recall/arm/…). `go=pressure*` still place-only.

## Antipatterns

- Offering export/checkpoint ritual on L1.
- Teaching «re-ARM before ending turn» under autonomous while a TM leaf is started (pre-0.5.540) — looks like work, is sleep.
- Stashing without Domain when domain work is in flight.
- Trusting host compaction summary over memo line.
- Skipping reconcile self-steer and inventing from stale Domain.
- Forcing pull→reconcile→align→ready when SSOT already sufficient.
- Inventing «compaction 2.0» / auto chat-delete organ — ADCM already owns Persist→Partition; tempo recycle = glue StageClock pulse + pressure DoD + announce + Autoi new-chat (Cursor-clear stays human/host gate until API). See TM deferred *ADCM voluntary chat recycle by SA tempo*.
- Treating self-check as «думать лучше» or appending a refute on top of a poisoned thread — **авто-отравление** (epistemic quality ≠ ADCM volume): external SSOT check → snesti poisoned active context → rebuild from stash/TM/domain/tools; mark alone ≠ cure. Same class as Partition/new chat. See TM parked *auto-poisoning card · retract+rebuild…* · scratch `note-20260803-auto-poisoning-retract-rebuild.md`.
- Cold/compact biped mask: one-screen serial, «как глазами», узкий leaf без трубы — forget ε. Ritual: Autoi amnesia postfix + `playbook-pf-body-not-biped-v1.md`. Hard steer: dig/parallel in CDP, not human serial.
- Throughput biped: dig→one ship→wake→dig→one — **list → batch → ship** instead (Meta hosts, FileLines, CIDE pack). Autoi timer ≠ license for single-item mill.
- Ignoring structured `wave=` / `## wave` on stash when flying a throughput batch (0.5.645+).

## last_ship

- **2026-08-09 Face ADCM densify (0.5.711)** — citizen DialogMemory owns Face Persist/Partition/Rebuild; pressure axis stays `cdp_pressure` / `@intent pressure` + fat dialog AfferentLine (not SoftInstrument ADCM). Mentions SoftFL Face-owned alone.
- **anti-rooster L1 flip** — notify = already late if stamp/memo missing; SceneArmedHint / AGENT_REMINDER · 2026-08-05
- 0.5.645: stash/recall `wave` field · SA biped_mill · organs inventory/verify_wave · 2026-08-03
- 0.5.636: Autoi `ChargeAmnesiaPostfix` body≠biped recall (pipe/CDP dig·parallel) · playbook-pf-body-not-biped-v1 · 2026-08-03
- 0.5.567: citizen `@intent pressure` host-execute → `IdePressureChannel.Handle` (peer L1 stash/recall without Cursor MCP) · VL #74 · 2026-08-03
- 0.5.540: pressure tips under autonomous — insurance / keep flying, not end-turn park (`AutoIgnitionChecklistLine` · `SceneArmedHint` · `StashHint`)
- 0.5.411 recall SSOT auto-ready (tax cut; ADR-0024 amend)
- 0.5.324 recall gate pull→reconcile→align→ready (CDP-ADR-0024)
