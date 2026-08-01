# Domain card: IdeRepl (cockpit cmd=)

- id: `iderepl`
- organ: `IdeRepl` / cockpit `cmd=`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `IdeRepl` is `partial` across verb slices.
- `Apply` is a thin router: `if (TryX(...) is { } hit) return hit;` then unknown → `Err`.
- Verb handlers: `Try*` return `(Args, Direct)?` — **null = not handled** (preserve fallthrough); do not rewrite to `bool + out` without need.
- Partials: Desk · Organs · Board · Ops · Share · Crm · Helpers (tokenize/help/meta parsers).

## Entry

- `IdeRepl.Apply` · `IdeRepl.Desk|Organs|Board|Ops|Share|Crm|Helpers`

## Antipatterns

- Re-inlining giant `Apply` into one file past soft-warn.
- Peel that rewrites `return (merged, X)` into always-`true`/`direct=` — breaks multi-line `Err(...)` and brace-less `if`.
- Treating Helpers (~395) as free room — stay ≤400.

## last_ship

- soft-warn peel: Desk/Organs/Board/Ops/Share/Crm/Helpers + Apply router @ 0.5.393; Helpers~395 · Apply~59
