# Domain card: Onboard (first-contact)

- id: `onboard`
- organ: `go=onboard_desk` · `cdp_onboard` · `man tool=first_contact`
- product: `#CDP`

## Invariants

- **Cold contact:** `cdp_open` → `onboard_desk op=scan` → entrypoints/next[] → then `go=plan`. Unbound wake must not invent plan before scan.
- **Desk Cap:** when `phase ∈ {explore, recall, clarify}` and `!OnboardHasScan`, `n-onboard` is reserved near top of `next[]` (under Cap=8) — not buried at ~21st.
- **After scan:** desk may merge top tip (Open entrypoint / README) from onboard `BuildNext`.
- Onboard = cold-start map of ProjectRoot — not VS Code Map, not invent.

## Entry

- `go=onboard_desk` · `op=scan|scene|clear`
- Meta: `cdp_onboard` (soft)
- Man: `cdp_man tool=first_contact` · `tool=buffer` for edit SSOT
- Canon: agent-notes `playbook-first-contact-attractor-v1`

## last_ship

- 2026-09-19 — L2 first-contact: desk top-3 n-onboard · man tool=first_contact · WakeUnbound open→scan→plan
