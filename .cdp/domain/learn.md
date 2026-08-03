# Domain card: IdeLearnChannel (learn)

- id: `learn`
- organ: `learn` / IdeLearnChannel / Meta `cdp_learn`
- product: `#CDP`

## Invariants

- Ops: scene|stash|list|recall|promote (aliases help/status→scene, capture/note/write→stash, get/peek→recall, export→promote).
- Journal under `StateRoot/learn-journal.jsonl`; promote → agent-notes `work/projects/_learn` (+ `.cdp/learn` mirror).
- Not findings (file memos) and not TM.

## Entry

- `go=learn` · aliases `learn_desk` / `learning` / `cdp_learn` · `IdeLearnChannel.Handle`
- Citizen host-execute: `@intent learn|learn_desk|cdp_learn|learning` → `CitizenRouteHost.RunLearn` → Meta `cdp_learn` (no steal `go=learn`)

## Antipatterns

- Treating pressure L1 stash as learn cards (different organ).
- Stealing bare `go=learn` into Verb.Learn — desk place must stay Verb.Go.

## last_ship

- **0.5.627** — citizen `@intent learn|*` host-execute Meta `cdp_learn` + PlaceOrgan(learn); ops scene/stash/list/recall/promote; no steal go=learn · tests CitizenLearnHostTests 7/7 · 2026-08-03
