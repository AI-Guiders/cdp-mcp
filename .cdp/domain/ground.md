# Domain card: Ground (same-wake)

- id: `ground`
- organ: `cdp_ground` / `go=ground`
- product: `#CDP`

## Pulse

Same-wake soft recalibration: detect → name (1 line) → next tool → resume. Soft mirror only — never hard block v1, never blame.

## Patterns

| Pattern | Next |
|---------|------|
| `buffer_mill` (≥5 buffer ops) | `go=inventory` |
| `ignore_hint` (mutate before recall, full wake) | `go=pressure` / `op=recall` once |
| `early_act` | `go=inventory` |
| `biped` | desk already surfaces inventory + wave seed |
| `ok` | resume |

## Entry

- `cdp_ground op=pulse|scene|reset|mark_recall`
- Desk `next[]` prefixes ground+inventory when buffer_mill hot
- Edit JSON may carry `ground.ignore_hint` once per wake
- Awareness: `.cdp/rules/awareness-same-wake.md`
- ADR: `docs/adr/CDP-ADR-0231-same-wake-ground.md`

## last_ship

- L5 same-wake ground — IdeGroundChannel + IdeSameWakeLatch + DeskNext buffer_mill + ADR-0231
