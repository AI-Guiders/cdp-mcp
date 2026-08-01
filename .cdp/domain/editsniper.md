# Domain card: EditSniper

- id: `editsniper`
- organ: `cdp_edit_sniper`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `EditSniper` is `partial` by concern.
- Partials: Core (hold/AimAtWire) · Ops (Dispatch/Scope/Target) · PeekResolve · Syntax (Roslyn expand/helpers).
- Process: sight → lock → arm → fire → verify; fire hard-blocked until `phase=armed`.
- Prefer semantic wires [F:;M:;K:] over bare L: (line_literal).

## Entry

- `EditSniper.Dispatch` / `AimAtWire` / `TryGetHold`

## Antipatterns

- Re-inlining Scope/Peek/Roslyn expand past soft-warn.
- Treating peek ritual as required before fire (armed is enough).

## last_ship

- soft-warn peel: Core277 Ops330 PeekResolve320 Syntax233 @ 0.5.397
