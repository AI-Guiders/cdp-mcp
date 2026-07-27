# CdpMcp.ArchitectureAnalyzers

Roslyn gun for **cdp-mcp desk wire** — same idea as CascadeIDE **CASCOPE***, adapted for non-Avalonia habitat (seats/TUI).

Normative: cascade-ide ADR [0197](../../cascade-ide/docs/adr/0197-cdp-mcp-cockpit-wire-parity-vs-cide.md), [0200](../../cascade-ide/docs/adr/0200-cdpcope-architecture-analyzers-desk-wire.md), CIDE [0036](../../cascade-ide/docs/adr/0036-cds-channel-compositor-surface-pipeline.md) / [0102](../../cascade-ide/docs/adr/0102-data-acquisition-layer-boundary-and-contract.md).

| ID | Level | Meaning |
|----|-------|---------|
| **CDPCOPE001** | Error | `IdeCockpit.Channel|Cds|Compositor` (+ `Cockpit/Channels|Cds|Composition`) must not `using Avalonia*` |
| **CDPCOPE016** | Error | `IdeCockpit.Ids` / `IdeDisplay/` must not `using Avalonia*` |
| **CDPCOPE020** | Warning | Same peels + `IdeCockpit.Build` / `ComputingUnits`: no direct `File`/`Process`/`HttpClient`/… — I/O in `Cockpit/DataAcquisition` |

Wired into `CdpMcp.csproj` as Analyzer (`ReferenceOutputAssembly=false`).
