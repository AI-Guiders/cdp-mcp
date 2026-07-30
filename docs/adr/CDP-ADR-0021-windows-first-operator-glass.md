# CDP-ADR-0021: Windows-first operator glass (Avalonia on hold)

**Status:** accepted (operator steer 2026-07-30)  
**Tags:** #cdp #adr #cockpit #cide #windows-first #wpf  
**Related:** [CDP-ADR-0019](./CDP-ADR-0019-icm-cdp-first-command-module.md) · [icm-anchor-start-stop-contract.md](../design/icm-anchor-start-stop-contract.md) · [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md) · CdpDeskTui spike

---

## Context

Operator glass (Cascade IDE / dual-cockpit Forward=Intercom, MFD, Semantic Map) was built on **Avalonia** for cross-platform aspiration. Daily reality: operator is on **Windows**; Avalonia’s component ecosystem is thin — anything past basic chrome becomes custom and burns attention that should go to Intercom / seats / graphs.

CdpDeskTui spike showed density/Pascal feel is useful, but TUI alone does not carry long Forward dialogue or real graph chrome. Cross-platform via Avalonia did not pay for itself for *this* operator cockpit.

## Decision

1. **Operator glass = Windows-first.** Default stack target: **WPF** (rich controls, text, trees, graph hosts without inventing a toolkit).
2. **Avalonia CIDE = on hold.** No new Avalonia feature peels unless emergency dogfood / security. Existing Avalonia may keep running as legacy projector until WPF host can Start/Stop via `cdp_cockpit_host`.
3. **Habitat (cdp-mcp) stays platform-agnostic.** Desk ABI, latches, Intercom wire, citizen frames — unchanged SSOT. Only the *human projector* changes toolkit.
4. **CdpDeskTui** remains an optional density companion (not a replacement for Forward long-form or Semantic Map glass).
5. **Cross-platform operator IDE** is deferred explicitly — not promised “from day one” via Avalonia.

## Consequences

- `cdp_cockpit_host` grows toward `projector=wpf|avalonia(legacy)|tui` (or multi-exe registry in toml); default Start → WPF when ready.
- New peels prioritize: thin WPF host + latch subscribe (presentation / intercom / land) → Forward=Intercom seat → MFD pages → graph surfaces.
- Do **not** big-bang rewrite Avalonia overnight; freeze and grow WPF beside it.
- Docs/ADR that say “Avalonia shell” for operator chrome should point here for current north star.

## Non-goals (this ADR)

- Delete Avalonia repo / force Linux operator shell.
- Move agent habitat onto WPF.
- Claim TUI replaces glass Intercom/graphs.

## Next peels (ordered)

1. Thin WPF host exe Start/Stop-compatible with existing latches.
2. Forward = Intercom long-read/write seat (operator style).
3. MFD page shell + one graph surface (Semantic Map adjacency → visual).
