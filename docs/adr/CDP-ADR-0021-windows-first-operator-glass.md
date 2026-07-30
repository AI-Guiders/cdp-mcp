# CDP-ADR-0021: Windows-first operator glass (Avalonia on hold)

**Status:** accepted (operator steer 2026-07-30; Endgame topology same day)  
**Tags:** #cdp #adr #cockpit #cide #windows-first #wpf #glass-cockpit  
**Related:** [CDP-ADR-0019](./CDP-ADR-0019-icm-cdp-first-command-module.md) · [icm-anchor-start-stop-contract.md](../design/icm-anchor-start-stop-contract.md) · [citizen-agent-wire-v0.md](../design/citizen-agent-wire-v0.md) · CdpDeskTui spike

---

## Context

Operator glass (Cascade IDE / dual-cockpit Forward=Intercom, MFD, Semantic Map) was built on **Avalonia** for cross-platform aspiration. Daily reality: operator is on **Windows**; Avalonia’s component ecosystem is thin — anything past basic chrome becomes custom and burns attention that should go to Intercom / seats / graphs.

CdpDeskTui spike showed density/Pascal feel is useful, but TUI alone does not carry long Forward dialogue or real graph chrome. Cross-platform via Avalonia did not pay for itself for *this* operator cockpit.

Separate-process glass (Start/Stop + `%LocalAppData%/cdp-mcp/*-LATEST.json` latches) is already the dual-cockpit wire. Endgame need not merge chrome into the habitat process.

## Decision

1. **Operator glass = Windows-first for daily dogfood.** Stack target: **WPF** (rich controls, text, trees, graph hosts without inventing a toolkit).
2. **Avalonia CIDE = on hold as Windows primary** — no new Avalonia feature peels unless emergency. Reposition Avalonia as the **Linux / non-Windows glass** lane when needed (not abandoned forever).
3. **Habitat (cdp-mcp) stays platform-agnostic.** Desk ABI, latches, Intercom wire, citizen frames — unchanged SSOT. Glass is a *projector process*, not a habitat dependency.
4. **CdpDeskTui** remains an optional density companion (not a replacement for Forward long-form or Semantic Map glass).
5. **Cross-platform is redefined:** CDP + chosen glass projector. Windows operators install WPF glass; Linux later via Avalonia glass — habitat does not become Windows-only.

### Endgame topology (solution include, not platform-lock)

```
CDP habitat (cdp-mcp)          — platform-agnostic agent cockpit
  ├─ latch wire (*-LATEST.json) — shared IPC
  ├─ CDP.GlassCockpit.Windows  — WPF projector (primary now)
  └─ CDP.GlassCockpit.Linux    — Avalonia / cascade-ide lineage (later / hold)
```

Endgame ≈ **add optional glass projects to the solution** (or sibling solutions), Start/Stop picks the host exe for the OS. The solution does **not** suddenly become Windows-only because WPF is present: conditional / optional projects; habitat builds without glass.

Naming sketch (repos may keep `CascadeIDE` / `cascade-ide` until rename): `CDP.GlassCockpit.Windows`, `CDP.GlassCockpit.Linux`.

## Consequences

- `cdp_cockpit_host` → `projector=wpf|avalonia|tui` (or multi-exe registry in toml); default Start on Windows → WPF when ready.
- Peels: thin WPF host + latch subscribe → Forward=Intercom → MFD → graphs. Grow beside Avalonia; freeze Avalonia feature work on Windows.
- Linux glass = later completion of Avalonia lane under GlassCockpit.Linux — not a promise of same-day parity.
- Docs that say “Avalonia shell” for operator chrome should distinguish: *Windows primary = WPF*; *Linux glass = Avalonia later*.

## Non-goals (this ADR)

- Delete Avalonia overnight / force Linux shell now.
- Move agent habitat onto WPF or make `CdpMcp.csproj` Windows-only.
- Claim TUI replaces glass Intercom/graphs.
- In-proc Endgame merge (glass inside MCP) — optional future, not required by this topology.

## Next peels (ordered)

1. Thin WPF host (`CDP.GlassCockpit.Windows` / under cascade-ide) Start/Stop + latches.
2. Forward = Intercom long-read/write seat (operator style).
3. MFD page shell + one graph surface (Semantic Map adjacency → visual).
4. When needed: rename/wire Avalonia as `CDP.GlassCockpit.Linux` without blocking Windows dogfood.
