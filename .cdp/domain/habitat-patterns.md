# Domain card: Habitat patterns (primitives)

- id: `habitat-patterns`
- organ: `CdpMcp.Habitat.RuleChain`
- product: `#CDP`
- contract: idiomatic C# primitives — not Platform, not MediatR

## Primitives

| Primitive | Role | When |
|-----------|------|------|
| `IRule<TContext,TResult>` + `RuleChain.FirstMatch` | Chain of Responsibility | 3+ branches with different semantics |
| `RuleChain.Pipe` | Decorator after match | policy wrap (e.g. pressure auto-full) |
| `record` context snapshot | Probe once | fire-time preflight (TM, stash) |

## Promote to Platform

Only when **≥2 products** share stable API. Until then: `cdp-mcp/Habitat/` internal.

## Invariants

- Prefer `Func` / `switch` for 1–2 branches — no rule chain.
- Data tables (aliases, markers) stay data — not `IRule`.
- Tests: one test file per primitive + organ rules.

## last_ship

- 2026-08-22: `Habitat/RuleChain.cs` + wake tier refactor (`0.5.740`)
