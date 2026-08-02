# Domain card: IdeRepl (cockpit cmd=)

- id: `iderepl`
- organ: `IdeRepl` / cockpit `cmd=`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `IdeRepl` is `partial` across verb slices.
- `Apply` is a thin router: `if (TryX(...) is { } hit) return hit;` then unknown → `Err`.
- Verb handlers: `Try*` return `(Args, Direct)?` — **null = not handled** (preserve fallthrough); do not rewrite to `bool + out` without need.
- Partials: Desk · Organs · Board(+Seed/Clock/Criteria) · Ops · Share · Crm · Helpers · Helpers.Title (title/@phase/#product meta).

## Entry

- `IdeRepl.Apply` · `IdeRepl.Desk|Organs|Board|Board.Seed|Board.Clock|Board.Criteria|Ops|Share|Crm|Helpers|Helpers.Title`

## Antipatterns

- Re-inlining giant `Apply` into one file past soft-warn.
- Peel that rewrites `return (merged, X)` into always-`true`/`direct=` — breaks multi-line `Err(...)` and brace-less `if`.
- Treating Helpers residual as free room — stay ≤400 (plugins parsers still live there).

## last_ship

- Board peel: Seed/Clock/Criteria under `IdeRepl.Board.cs` thin router (~17L) @ 0.5.456
- prior: soft-warn near-miss Helpers.Title165 · Helpers237 @ 0.5.404; Desk/Organs/Board/Ops/Share/Crm/Helpers + Apply router @ 0.5.393
