# Domain card: Glass 0-sync (dual-HCI)

- id: `glass`
- organs: WPF `CDP.GlassCockpit.Windows` · Avalonia projectors · CDP latches
- product: `#CIDE` / `#CDP`
- learn: `note-0-sync-glass-entity-parity` — every CDP desk entity needs CIDE/Glass presence (stub OK)

## Invariants

- CDP habitat = SSOT; Glass = projector (ADR-0021 Windows-first WPF).
- SoftOrgan chrome band ≠ EICAS: alert/qrh/ecl stay EICAS (not SoftOrganLatchCatalog).
- `sa_desk` SoftOrgan → quiet chrome (`sa-desk-LATEST` / WorkspaceChromeBand) — not MFD `Problems`, not EICAS `go=sa`.
- Quiet-chrome SoftOrgans (dedicated latch/projector): `sa_desk`, `crm`, `plugins`, `webcam` — presence = WorkspaceChromeBand, not force-MFD.
- Seat/chrome SoftOrgans: `plan` (P), `pressure` (L1), `sys` (legacy pulse; banner/board already carry slim status).
- SoftOrganLatchCatalog gates SoftOrgan `*-LATEST.json`; LatchHub routes alert/qrh/ecl separately.
- CabinGlassProjectionCatalog: every SoftOrganKind go-pin resolves (MfdPage or chrome_hint stub).
- Host start hydration (`CockpitHostLatchHydration`) must include SoftOrgan + EICAS latch names that exist on disk.
- `mcp` SoftOrgan → MFD `AiChatSettings` (MCP settings live there); MFD `Chat` = Intercom/citizen secondary — not mcp.

## Entry

- WPF: `LatchHub` · `EicasBandAggregator` · `LatchPaint.Eicas`
- Avalonia: `CdpEclProjector` · alert/qrh projectors
- CDP: `Cide*Latch` · `CabinGlassProjectionCatalog` · `CockpitHostLatchHydration`

## Antipatterns

- Stuffing alert/qrh into SoftOrgan band (tests explicitly ignore as EICAS bleed).
- Mapping `sa_desk` → MFD `Problems` (gates pulse paints WorkspaceChromeBand; Problems = quality/review family).
- Mapping `crm` → MFD `Correspondence` (CRM = await/callout chrome; CRS = doc↔code surface).
- Inventing SoftOrganKinds to bind orphan MFD pages (Events/Hypotheses/WorkspaceHealth/EnvironmentReadiness) — presence ≠ invent entity.
- Soft deploy ≠ remounted habitat; Glass WPF rebuild is separate from cdp-mcp seat.
- Mapping `mcp` SoftOrgan → MFD `Chat` (Chat = Intercom/citizen; MCP settings = AiChatSettings).

## last_ship

- 2026-08-01: WPF Glass ecl-LATEST EICAS wire (Avalonia parity) · cascade-ide `08cc8f34`
- 0.5.364: hydrate domain + sa-desk on cockpit host start
- 0.5.365: `arch_desk` → MFD `SemanticMap` (+ chrome hint) in CabinGlassProjectionCatalog
- 0.5.366: `domain`/`learn` → MFD `MarkdownPreview` (+ chrome hints) — domain cards & learn notes are md
- 0.5.367: `mcp` → MFD `AiChatSettings` (+ chrome hint); Chat MFD held for Intercom/citizen
- 0.5.368: `project_switch` → MFD `SolutionExplorer` (+ chrome hint)
- 0.5.369: `onboard` → MFD `MarkdownPreview` (+ chrome hint) — onboard docs are md
- 0.5.370: `review` → MFD `Problems` (+ chrome hint) — same family as quality/gates
- 0.5.371: `report`/`evidence`/`pfd` → MFD `MarkdownPreview` (+ chrome hint)
- 0.5.372: `toolchain` → MFD `Build` (+ chrome hint)
- 0.5.373: `refactor`/`debt` → MFD `RelatedFiles` (+ chrome hint) — blast/find_usages family with find_desk
- dig reject: `sa_desk` stays chrome (WorkspaceChromeBand / CdpSaDeskProjector) — not Problems
- dig close: `crm`/`plugins`/`webcam`/`plan`/`pressure`/`sys` intentional chrome (projectors or seat/banner); EICAS stay EICAS
- Glass reverse: MFD orphans `WorkspaceHealth`/`EnvironmentReadiness`/`Events`/`Hypotheses` added as WPF list+stub (CIDE enum parity)
- SoftOrgan peel DoD closed: every `SoftOrganKind` resolves (MFD or chrome); orphan MFDs stay unbound — do not invent SoftOrgan to fill CIDE pages; EnvironmentReadiness/IdeHealth are CCUs not SoftOrgans
- Chat/Intercom: primary presence = Glass Forward Intercom feed (latch/journal); MFD `Chat` stays secondary stub — not SoftOrgan bind
- MFD instrument glance: `Build`←`toolchain`, `Terminal`←`sys`, `Tests`←`test_desk`, `DebugStack`←`debug_desk`, `Problems`←`review`, `SemanticMap`←`arch`, `AiChatSettings`←`mcp`, `MarkdownPreview`←`report`, `RelatedFiles`←`refactor` (`SoftOrganMfdGlance`); field-enrich DoD closed for mapped SoftOrgans; ConPTY/MSBuild/Roslyn Problems/graph/mount/md/find_usages/DAP stack host later
- dig reject: Glass WPF Terminal ConPTY — SSOT = CIDE Avalonia `TerminalMfdPageView`/`IntegratedShellLaunch`; Glass stays `sys` latch glance until Windows terminal-control peel (do not fork PTY into TextBlock) · cascade-ide `131cc677`/`46be0e27`
- dig reject: Glass WPF Build MSBuild — SSOT = CIDE Avalonia `BuildMfdPageView`/`BuildOutputPanelViewModel`; Glass stays `toolchain` latch glance until WPF build-log host peel · cascade-ide `f2a6cf10`/`51655639`
- dig reject: Glass HybridIndex SoftOrgan glance — CabinGlass `hybrid_index`/`hci`/`codebase_index` → MFD only; no SoftOrganKind; live HCI SSOT = Avalonia `HybridIndexMfdPageView`/`HybridIndexOrchestrator` (do not invent SoftOrgan) · cascade-ide `31c962a9`
- 2026-08-01: DebugStack←`debug_desk` SoftOrganMfdGlance + `CideDebugDeskLatch` · cascade-ide `ad2e9b56` · cdp-mcp `ab90576`
- invariant: Glass SoftOrgan MFD glance = dual-HCI peel DoD for instrument *presence*; live Terminal/Build/Tests hosts stay on CIDE Avalonia (do not duplicate orchestration into Glass TextBlock)
- SolutionExplorer: Glass `.sln` project-list glance + flat WPF TreeView peel (`GlassSolutionExplorerGlance` / `MfdSolutionExplorerTree`); full tree SSOT = Avalonia `SolutionExplorerView` (do not fork nested file tree into Glass) · cascade-ide `fb1092ad`
- 2026-08-01: Tests←`test_desk` SoftOrganMfdGlance + `CideTestDeskLatch` · cascade-ide `061f607f` · cdp-mcp `6f25247`
- 2026-08-01: Avalonia `CdpTestDeskProjector` quiet chrome from `test_desk-LATEST` · cascade-ide `e01a5c86`
- 2026-08-01: SoftOrganMfdGlance Problems/SemanticMap field enrich · cascade-ide `313c650d`
- 2026-08-01: SoftOrganMfdGlance mcp/report field enrich · cascade-ide `8598e179`
- 2026-08-01: SoftOrganMfdGlance RelatedFiles/refactor field enrich · cascade-ide `2d0190c2`
