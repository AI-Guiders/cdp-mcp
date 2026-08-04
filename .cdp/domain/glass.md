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
- Intercom partner presence = separate latch `intercom-presence-LATEST.json` (idle|composing|busy + reader stale) — do **not** mix into voice/journal, SoftOrgan, EICAS, or host-start hydration (fake freshness).
- `mcp` SoftOrgan → MFD `AiChatSettings` (MCP settings live there); MFD `Chat` = Intercom/citizen secondary — not mcp.

## Entry

- WPF: `LatchHub` · `EicasBandAggregator` · `LatchPaint` (seats+land+shared+disk+ignite-wake) · `MainWindow.SeatsSurface` · `MainWindow.LandSurface` · `MainWindow.SharedSurface` · `MainWindow.DiskSurface` · `MainWindow.IgniteWakeSurface`
- Avalonia: `CdpEclProjector` · alert/qrh projectors
- CDP: `Cide*Latch` · `CabinGlassProjectionCatalog` · `CockpitHostLatchHydration`

## Antipatterns

- Stuffing alert/qrh into SoftOrgan band (tests explicitly ignore as EICAS bleed).
- Mapping `sa_desk` → MFD `Problems` (gates pulse paints WorkspaceChromeBand; Problems = quality/review family).
- Mapping `crm` → MFD `Correspondence` (CRM = await/callout chrome; CRS = doc↔code surface).
- Inventing SoftOrganKinds to bind orphan MFD pages (Events/Hypotheses/WorkspaceHealth/EnvironmentReadiness) — presence ≠ invent entity.
- SoftOrganMfdGlance remapping RelatedFiles ← find_desk (stays ←refactor; FindDesk DoD = CabinGlass pin+chrome, not glance displace).
- Inventing SoftOrganKind for crs/Correspondence latch+chrome (CabinGlass MFD pin only; Crm chrome ≠ CRS; SoftOrganMfdGlance stays unbound).
- Soft deploy ≠ remounted habitat; Glass WPF rebuild is separate from cdp-mcp seat.
- Intercom MarkdownBody as ContentControl setting Content during DataTemplate expand — double-parents built tree (cabin crash); use StackPanel+deferred rebuild.
- Treat Glass Ctrl+Q `c:` as GlassChord-only aliases — SSOT is `IntentMelody/intent-catalog.toml` via `GlassIntentMelodyCatalog`; chords stay on Ctrl+K.
- Mapping `mcp` SoftOrgan → MFD `Chat` (Chat = Intercom/citizen; MCP settings = AiChatSettings).
- Festive per-zone accents (cyan P / gold F / purple M) — Dark Cockpit violation; geography by label, color only on deviation (ON GND / select / EICAS).
- MFD text-wall dig notes for humans — prefer concise+graphic presence cards (`□ Glass peel` / `■ Avalonia`).
- Invent SoftOrgan for Editor MFD when Forward=intercom — AvalonEdit peel already mounts on M (ADR 0120).
- Fork Glass WPF full IdeDapDebugSession into TextBlock — densest = habitat `debug_desk` latch live (stack/locals on stopped) + SoftOrgan FSW; Avalonia IdeDap remains denser SSOT for full DAP drive UI.
- Fork Glass WPF Git panel into TextBlock — densest = Process-redirect porcelain+stage/commit on host (`GlassGitProcess`); no SoftOrganKind; Avalonia `GitPanel` remains denser SSOT (push/submodule).
- Treat HybridIndex as empty □/■ stub forever — Glass FS status glance (`GlassHybridIndexGlance`) is the presence peel; Avalonia HCI remains SSOT (do not invent SoftOrgan).
- Treat WorkspaceHealth / EnvironmentReadiness as empty □/■ stubs forever — Glass FS/env presence glances (`GlassWorkspaceHealthGlance` / `GlassEnvironmentReadinessGlance`); Avalonia IdeHealth / EnvReady CCU remain SSOT (do not invent SoftOrgan).
- Treat Events / Hypotheses as empty □/■ stubs forever — Glass latch/catalog + JSON status glances (`GlassEventsGlance` / `GlassHypothesesGlance`); Avalonia EventsMFD / HypothesesMfd remain SSOT (do not invent SoftOrgan).
- Invent `MfdShellPage.SemanticMap` to "fix" Glass/CabinGlass string parity — Avalonia graph is PFD `WorkspaceNavigationMapView`; Glass `SemanticMap` = arch projector alias + SoftOrganMfdGlance (do not invent Avalonia MFD page).
- Invent SoftOrgan latches for Ps1Desk/MdAuthor to paint SoftOrganBand — CabinGlass already resolves MFD Terminal/MarkdownPreview; SoftOrganBand stays latch-first (do not invent SoftOrgan).
- Treat Glass SoftOrganBand cabin chrome as inventing `cabin-LATEST` SoftOrgan writer — cabin chrome comes from `seats-LATEST` peel (`LatchHub.SeatsChanged`), same as Avalonia `CdpSeatsProjector`.
- Treat `shared`/`land`/`disk` as SoftOrgan catalog fills — they are dual-cockpit latches with dedicated Avalonia projectors; Glass peels them separately (do not invent SoftOrgan ids).

## DoD matrix — MfdShellPage presence (2026-08-01 dig)

Presence DoD = SoftOrgan glance | Glass peel | **full WPF host** (operator 2026-08-03 unHOLD: design polish later). Do **not** invent SoftOrgan to fill cells.

| Page | SoftOrgan | Glass peel / host | Verdict |
|------|-----------|-------------------|--------|
| WorkspaceHealth | unbound | `GlassWorkspaceHealthGlance` FS | DONE peel |
| EnvironmentReadiness | unbound | `GlassEnvironmentReadinessGlance` env | DONE peel |
| Events | unbound | `GlassEventsGlance` latch/catalog | DONE peel |
| Hypotheses | unbound | `GlassHypothesesGlance` JSON | DONE peel |
| HybridIndex | unbound (no SoftOrgan invent) | `GlassHybridIndexGlance` + live `GlassHybridIndexStatusProbe` host | DONE host |
| SolutionExplorer | unbound | TreeView nested + `GlassSolutionExplorerGlance` | DONE tree |
| Chat | unbound | `GlassIntercomPresence` card | DONE presence |
| Editor | unbound | AvalonEdit on M when Forward=intercom | DONE peel |
| Terminal | `sys` glance | EasyWindowsTerminalControl (WT VT) | DONE VT · Avalonia EOL |
| Build | `toolchain` glance | log + `BuildProblemsList` MSBuild parse | DONE host |
| Tests | `test_desk` glance | log + `TestsFailList` (`GlassTestOutputParse`) | DONE host |
| Git | unbound | porcelain + stage/commit/push/submodule (`GlassGitProcess`) | DONE depth |
| DebugStack | `debug_desk` glance | latch stack/locals + Continue/Step via `GlassDapCommandBridge` | DONE dap-full densest |
| Problems | `review` glance | ListBox + MSBuild + Roslyn syntax merge | DONE roslyn merge |
| RelatedFiles | `refactor` glance | WNM-shaped feed + `GlassRelatedFilesIdeProbe` HCI | DONE rf-idemcp densest |
| SemanticMap | `arch` glance | Skia + multi-hop `GlassSemanticMapGraph` | DONE sm-wnm densest |
| MarkdownPreview | `report` glance | Markdig FlowDocument (`GlassMarkdownFlowDocumentBuilder`) | DONE md-rich |
| AiChatSettings | `mcp` glance | live host + `GlassAiChatSettingsGlance` | DONE aichat-host |
| WebAiPortal | unbound | WebView2 embedded | DONE VT-class · Avalonia EOL |
| Correspondence | unbound | full CRS resolvers (Toml ADR/feature + reverse anchors + layers) | DONE full CRS |

Sources: `Models/MfdShellPage.cs` · `SoftOrganMfdGlance.TryOrganIdForMfdPage` · `MainWindow.MfdBody` · XAML `MfdPages`.

Parity note: Glass XAML / CabinGlass use page string **SemanticMap** (`arch_desk` → MFD); Avalonia `MfdShellPage` has **no** SemanticMap member — graph SSOT = PFD `WorkspaceNavigationMapView` (not MFD shell).

**Presence DoD: CLOSED**. **Full-host v1 DoD: CLOSED** (2026-08-03 batch `77035101`+`f7de96e9`) — CascadeChord = Ctrl+K overlay (not MFD). **Depth DoD: CLOSED** — full CRS resolver (Peel15 GlassCore). **Terminal VT: CLOSED** · **WebView2 WebAi: CLOSED** · **SemanticMap Skia: CLOSED** · **DebugStack live DAP: CLOSED** · **Correspondence full CRS: CLOSED**. **CIDE-Glass-residual wave (14): CLOSED** (2026-08-04 densest overnight — md-rich/rf/git-depth/dap/sm/chord/aichat/se/build/test/hci/msg-code/problems-roslyn/ide-mcp; Avalonia still denser for IdeDap/WNM full; design polish later).

## DoD matrix — SoftOrgan chrome beyond MFD (2026-08-01 dig)

Chrome DoD = SoftOrgan latch → Glass SoftOrganBand · Avalonia Cdp*Projector · CabinGlass pin. SoftOrganBand is **latch-first** (`SoftOrganLatchCatalog` only).

| SoftOrganKind | Latch | Glass band | Avalonia | Verdict |
|---------------|-------|------------|----------|---------|
| Plan/Pressure/Sys/Plugins/Crm/Webcam/SaDesk | `{id}-LATEST` | SoftOrganBand | Cdp*Projector | DONE |
| FindDesk/FilesDesk/TestDesk/DebugDesk/Toolchain/Review/Refactor/Report/Arch/Ignite/Onboard/Learn/Domain | latch | SoftOrganBand | Cdp*Projector | DONE |
| ProjectSwitch | `scope-LATEST` | SoftOrganBand via `scope` | CdpScopeProjector | DONE |
| Alert/Qrh/Ecl | EICAS latches | EICAS band (not SoftOrgan) | CdpAlert/Qrh/Ecl | EICAS HOLD |
| Problems/Quality/BuildDesk | none SoftOrgan | — | — | CabinGlass MFD only |
| Ps1Desk / MdAuthor | **none** | chrome_hint dropped | no projector | DIG REJECT invent SoftOrgan latch — presence = Terminal / MarkdownPreview MFD |
| (catalog) `cabin` | reserved (via seats) | SoftOrganBand Apply(`cabin`) from `seats-LATEST` | Avalonia `CdpSeatsProjector` → `AgentCabinChromeHint` | DONE seats peel |

**Seats peel DoD: CLOSED** — Glass watches `seats-LATEST` (`LatchHub.SeatsChanged` → `LatchPaint.PaintSeats` → `SelectMfdPage` + SoftOrganBand cabin).

## DoD matrix — residual dual-cockpit latches beyond SoftOrgan/MFD (2026-08-01 dig)

After MFD presence + SoftOrgan chrome closed: Avalonia still has non-catalog latch projectors that Glass LatchHub does **not** peel. Presence ≠ invent SoftOrgan.

| Latch | Schema / role | Avalonia | Glass | Verdict |
|-------|---------------|----------|-------|---------|
| `land-LATEST` | `navigation_land_latch/v1` agent `cdp_land` open\|goto | `CdpLandProjector` → OpenFile/GoToPosition | SoftOrganBand N/A · `LatchHub.LandChanged` → AvalonEdit | DONE land peel |
| `shared-LATEST` | `shared_file_latch/v1` co-presence (human focus ∩ agent buffers) | `CdpSharedFileProjector` → tab ` · shared` | `LatchHub.SharedChanged` → EditorPathLabel | DONE shared peel |
| `disk-LATEST` | `document_disk_sync_latch/v1` Instant Save → reload | `CdpDiskSyncProjector` → Monaco reload | `LatchHub.DiskChanged` → AvalonEdit Load | DONE disk peel |
| `ignite-wake-LATEST` | `ignite_wake_latch/v0` AutoI wake charge | (Composer/CDT adapter; Intercom mirror) | SoftOrgan `ignite` tip · StatusText · FDS WAKE | DONE Autoi wake consumer |

Hydration already lists all three (`CockpitHostLatchHydration`). SoftOrganLatchCatalog does **not** include `shared`/`land`/`disk` — correct (not SoftOrgan).

**Land peel DoD: CLOSED** — Glass watches `land-LATEST` (`LatchPaint.PaintLand` → `OpenCodeFile(path, line)`).

**Shared peel DoD: CLOSED** — Glass watches `shared-LATEST` (`LatchPaint.PaintShared` → `EditorPathLabel` + ` · shared` when path match); UIA dogfood LatchHub.cs.

**Disk peel DoD: CLOSED** — Glass watches `disk-LATEST` (`LatchPaint.PaintDisk` origin=agent → `OpenCodeFile` reload + human Save publish); UIA dogfood ` · disk` on EditorPathLabel.

**Residual latch triad: CLOSED** (land + shared + disk).


**Post-triad invent dig: CLOSED** — Avalonia dual-cockpit projectors (Disk/Land/Shared/Seats/Presentation/Intercom/Presence/Alert/Qrh/Ecl) all have Glass LatchHub peels; SoftOrgan path covers catalog SoftOrgans; hydration minus intercom-presence (intentional). Remaining HOLD = DIG REJECT Avalonia SSOT (DAP/Problems/RelatedFiles/SemanticMap/Markdig/WebAiPortal/Correspondence). No open act peel leaf.

**Glass 0-sync entity presence epic: CLOSED** for catalog completeness (stub OK). Next board = soft-warn peel / other epic — not orphan Glass entity.

## DoD matrix — lived Glass UX for standalone 15.08 (2026-08-02 dig)

North star: **standalone CDP without Cursor** · dialog peer on Glass/Intercom (wire-citizen = hands only).

| Area | State | Verdict |
|------|-------|---------|
| Ctrl+K / Ctrl+Q | Full peeled-MFD reach `fd/at/op/mb/ms/mg/sx/hi/wh/er/ev/hy/ic/cz` + process MFDs; **Ctrl+Q `c:`** → GlassChord melody list+Help (discoverability peel; ≠ full intent-catalog) | **P3 shipped** |
| EICAS | Assembled stack + per-severity chips (`MfdHealthBand` / `BandChips`) · CLEAR when idle | **P1 shipped** (`dfe4ace9`) |
| Topic cards | XAML/`GlassIntercomTopics` + empty hint; **30m** quiet-gap cluster · follow-newest on stickEnd · `/topics N` · cluster tail 240 | **P0b shipped** |
| Message↔code | `/open` + journal chips · disk resolve chrome · strip brackets · range select; Avalonia Skia still fuller | **P2 shipped** |
| FDS (Flight Data Storage) | MFD shelf peels plan/shared/report/pressure · `/fds` | **P1 shipped** |
| Intercom identity | `Кир · guest` / `Света · operator` / `Citizen · citizen` RoleLabel · Who ≠ operator | **P0 shipped** |
| Intercom → citizen dialog | Glass `/citizen` → request latch → habitat bridge → Intercom citizen reply · chord `cz` · **hands Execute+PeerAck 0.5.561** | **P0 shipped (0.5.496+561)** |

### P0→P3

- **P0** Intercom `name` + `kind` (guest|citizen|operator) on latch + Glass RoleLabel (`Кир · guest @PF → @PM`)
- **P0b** Topics visible + attach↔code gap close on Glass
- **P1** Clearer EICAS band · FDS M-page skeleton — **shipped**
- **P2** Chord depth vs Avalonia · message↔code thin chips — **message↔code shipped**; full Avalonia CascadeChord still HOLD
- **P3** Lived polish / one-workspace survival — **shipped** (FileLines + peeled-MFD keyboard reach)

### P3 dig 2026-08-02 — one-workspace survival

| Gap | Verdict |
|-----|---------|
| MainWindow* FileLines ≥400 | **CLOSED** |
| Process MFDs keyboard | **CLOSED** (`mt/mb/ms/mg`) |
| Peeled glance MFDs keyboard | **CLOSED** (`sx/hi/wh/er/ev/hy/ic`) |
| Dialog peer `/citizen` keyboard | **CLOSED** (`cz`) |
| Citizen full chain Glass↔habitat | **CLOSED** (request latch + bridge + status watcher) |
| Full Avalonia CascadeChord | **ACT full** (wave) |
| Agent surface Aim/Drive | **CLOSED** (see `surface.md`) |

**Lived Glass UX epic: CLOSED** for 15.08 one-cockpit survival DoD. **Citizen full chain: CLOSED** (live latch dogfood + StatusText pending→running→done/error).

### Dig 2026-08-03 — CascadeChord + full hosts unHOLD

- **Operator steer:** полные WPF-хосты из Avalonia; дизайн потом.
- **Verdict: unHOLD.** DIG REJECT Avalonia-SSOT cancelled for Problems/RelatedFiles/SemanticMap/Markdig/DAP/ConPTY/WebAi/CRS/Git panel/CascadeChord full.
- Wave on TM: problems-host → build-full → git-panel → markdown-host → cascade-chord → related-files → correspondence → debug-dap → terminal-conpty → webai-portal → semantic-map.

## last_ship
- **2026-08-04 Voice Letter #157** — Intercom topic IOP lived (surface topic_next); DIG ACCEPT Glass Done leaves closed (board zombie ≠ SoftFL mill).
- **2026-08-04 cabin live** — Rebuild Glass Release (stale exe unknown topic_next); surface `run action=topic_next` → `glass · topic next · …`. Glass Done feature closed. Voice Letter → #157.
- 2026-08-04: Intercom keyboard IOP — melody atn/atp/atb/ato/amn/amp → topic_next/prev/topics_all/slash_open/feed_page; GlassIntercomTopicNav + RunPaletteEntry; tests 21/21 · cascade-ide `c041ad09`
- **2026-08-04 night invent** — PathMutate/Autoi duplex DIG ACCEPT shipped 0.5.649 (place=before body + Autoi-off respect). Glass/CIDE residual DIG REJECT reopen. Citizen full-chain live GREEN. Densest after remount: invent product beyond SoftFL/Meta/Glass CLOSED (not board hygiene).
- **2026-08-04 SickLeaveNight648** — dig-ignite hygiene (error+stale arms cleared); glass/CIDE DIG REJECT reopen (VT/WebView2/Skia/DAP/CRS Depth DoD already 2026-08-04); product next = PathMutate vs Autoi duplex densest dig.
- 2026-08-04: **CIDE-Glass-residual wave CLOSED** — 14 densest peels (FlowDocument md · HCI RelatedFiles · git push/submodule · DAP step bridge · SemanticMap multi-hop · Chord AwaitMelodyTail · AiChat host · SE tree · build/test fail lists · HybridIndex status probe · msg-code chips · Roslyn problems merge · palette prefix allowlist) · Glass tests 92/92 · tip `0641be14` · SoftFL CLOSED
- 2026-08-04: **Glass residual Git+RelatedFiles** — `GlassGitProcess` stage/unstage/commit · `GlassRelatedFilesFeed` WNM-shaped list · tests 29/29 · cabin pid live · SoftFL CLOSED
- 2026-08-04: **Glass Correspondence full CRS** — Peel15 CRS DAL→GlassCore · feed wires WorkspaceCorrespondenceResolver+DocReverseAnchorResolver+layers · smoke F=14 R=1 on DocReverseAnchorResolver · Depth DoD CLOSED
- 2026-08-04: **Glass DebugStack live DAP** — SoftOrganChanged→MFD refresh · latch stack/locals enrich on DAP stopped · dogfood `mfd_debug_stack` → `MfdDebugStackHost` visible + latch frames
- 2026-08-04: **Glass SemanticMap Skia** — WriteableBitmap+SkiaSharp radial graph · RelatedFiles heuristic · live dogfood `semantic · skia N · click node` + SemanticSkia visible
- 2026-08-04: **Glass WebAi WebView2** — embedded Microsoft.Web.WebView2 in MFD · URL bar + go/Enter · Avalonia portal EOL for cabin
- 2026-08-04: **Glass Terminal VT focus fix** — start after layout size · Focus steal from Composer · `c66d0c25`
- 2026-08-04: **Glass Terminal VT** — EasyWindowsTerminalControl (WT WPF) · Avalonia EOL steer · launch via GlassCore IntegratedShellLaunch · TextBox/GlassConPtyShell removed from MFD
- 2026-08-03: **Glass ConPTY session shared** — Terminal DAL linked into GlassCore · (superseded by VT host above for Glass MFD)
- 2026-08-03: **dig Glass Terminal ConPTY peel path** — DAL Avalonia-free; UI SSOT AvaloniaTerminal; extract→GlassCore + Glass host path stamped
- 2026-08-03: **Glass palette+chord mfd_*** — `mp/rf/sm/cr/md/ds/wa` · cascade-ide `fc631619` · surface dogfood 7/7
- 2026-08-03: **Glass MFD host batch v1** — feeds `77035101` + WPF hosts `f7de96e9` · SoftFL CLOSED · **depth next:** WebView2/Skia/live-DAP/full-CRS
- 2026-08-03: **operator unHOLD** full Avalonia→Glass WPF hosts (design later) · wave 11 items · shipping Problems first
- 2026-08-03: Glass Intercom markdown crash — ContentControl+DataTemplate double-parent + shared TextDecorations.Underline killed cabin; StackPanel+deferred rebuild + frozen underline · cascade-ide `5ffc45f2` · lived start pid dual-cockpit ICM
- 2026-08-03: cockpit_host cfg latch — toml mtime refresh + Start stamp so path=/toml Glass rediscover stays honest without remount · cdp-mcp `36b5355`
- 2026-08-03: cabin SSOT — cdp-debug [cockpit_host] exe was Avalonia CascadeIDE; pointed to Glass WPF + live start → CDP.GlassCockpit.Windows · ICM bound · Avalonia = agent-IDE/HOLD instruments, not operator cabin
- 2026-08-03: **0.5.590** citizen `@intent land|deep_link` host-execute → same `cdp_land` / `land-LATEST` latch Glass already peels (peer nav without Cursor MCP)
- 2026-08-03: Glass→Citizen **PeerAck surface** — habitat appends peer tip to Intercom + latch `peer=`; Glass StatusText paints done·peer · cdp-mcp **0.5.565** · lived latch `c27ace9928e9`
- 2026-08-03: **lived** unforced Glass→Citizen→Cloud.ru latch dogfood — RU soft ask → FM `@intent git` → seats `M:git` + Intercom citizen (hands 0.5.561 proved beyond stuffed health)
- 2026-08-03: Glass→Citizen **hands parity** — habitat `CitizenGlassDialogBridge` Execute+PeerAck after dialog Turn (talk≠hands closed) · cdp-mcp **0.5.561**
- 2026-08-03: Glass **surface `op=run`** — `AgentSurfaceRun` (action|command_id|text=/slash) without Ctrl+Q; live dogfood `slash_status` + `get_ide_state` + `mfd_git` from cockpit; cascade-ide `a71e2c4e` · cdp-mcp **0.5.559** · VL #64
- 2026-08-03: Glass **c: cabin peels** — `save_document`/`focus_composer`/`glass.slash_*`/`glass.mfd_*` → existing RunPaletteEntry; live latch `c:sf`/`c:fc`/`c:cz`/`c:wh`/`c:sh`→`/help`; cascade-ide `c24c4ca1` · VL #61
- 2026-08-03: Glass **c: allowlist widen** — `open_file`/`open_file_dialog`→`open_file`; `intercom.attach_*`→`slash_attach`; melody `of`/`fe`; live latch dogfood `c:of`/`c:ias`/`c:fe`; cascade-ide `dad4d678` · VL #60
- 2026-08-03: Glass **surface `palette`** + SendInput chords + HostAccess null-safe boot — live cabin dogfood `c:st`→`/status` IOP feed; cascade-ide 366fb28f+2d020f9f · VL #59
- 2026-08-03: Glass **/status IOP glance** — `GlassIopStatusGlance` (editor/caret/dirty/mfd/topology/latch) · `c:st`/`get_ide_state` → `slash_status`; DIG REJECT full Avalonia get_ide_state JSON · cascade-ide `2662d9d5` · tests 15/15
- 2026-08-03: Glass **`c:els` line select** — `GlassMelodyTail` + `SelectOpenDocumentLines`; parametric `els:L` / `els:L:L`; DIG REJECT eld/esc/full parametric · cascade-ide `6354112d` · tests 13/13
- 2026-08-03: Glass Ctrl+Q **`c:` allowlist execute** — `GlassMelodyGlassActions` maps `git_status`/`build_*`/`run_tests`/`show_*` → redirected MFD peels; unmapped browse-only; DIG REJECT full IdeMcp · cascade-ide `67b9a0b1`
- 2026-08-03: Glass Ctrl+Q **`c:` → intent-catalog.toml** — `GlassIntentMelodyCatalog` thin Tomlyn peel (melody_slug + Help); discoverability rows `melody:{command_id}` non-exec; GlassChord stays Ctrl+K; tests 12/12 (≠ Ide execute in Glass)
- 2026-08-03: Glass Ctrl+Q **`c:` Command Melody** discoverability — `TryGetMelodyTail` + chord aliases with Help in palette; hint/no-match rows; tests 11/11 (superseded by intent-catalog peel above)
- 2026-08-02: Glass Autoi wake consumer — `IgniteWakeLatchFileName` · `LatchHub.IgniteWakeChanged` · `LatchPaint.PaintIgniteWake` · SoftOrgan tip + FDS WAKE · hydration · dogfood `dogfood-glass-wake-0.5.516` · VL #22 · seat 0.5.516
- 2026-08-02: **0.5.506** citizen replace/open → `land-LATEST` open (Glass feels invent; disk peel alone was skip-when-not-open)
- 2026-08-02: Citizen full chain close — Glass `LatchHub.CitizenDialogRequestChanged` + `CitizenDialogRequestStatus` StatusText; live latch E2E `pending→done` + Intercom `kind=citizen`; tests 2/2
- 2026-08-02: Glass full peeled-MFD chord/palette batch — `sx/hi/wh/er/ev/hy/ic` + matching palette ids; tests 8/8 · cascade-ide `65688504`
- 2026-08-02: Glass P3 survival keyboard reach — Ctrl+K `mg`/`cz` + palette `mfd_git`/`slash_citizen`; tests 8/8 · cascade-ide `55afb805`
- 2026-08-02: Glass MainWindow.xaml FileLines soft-warn peel — styles→`GlassChromeStyles` · overlays→`GlassFloatingOverlays` · MFD process hosts→`GlassMfdProcessHosts` · HostAccess forwards; 540→343 lines · cascade-ide `0f69213a`
- 2026-08-02: Glass message↔code thin chips P2 — `Resolved` disk chrome · `StripBracketsForDisplay` · AvalonEdit line-range select on chip click; tests 6/6 · cascade-ide `aa61d774`
- 2026-08-02: Glass EICAS band clearer P1 — `EicasBandAggregator.BandChips` + WPF `MfdHealthBand` per-severity chips (warn/caut/adv) like Avalonia `EicasAlertsBar`; CLEAR when empty; tests 2/2 · cascade-ide `dfe4ace9`
- 2026-08-02: Glass topics cluster deepen — follow-newest after 30m gap on stickEnd (`GlassIntercomTopicFollow`); cluster LoadTail 240; `/topics N` select; journal cap 500
- 2026-08-02: Glass Intercom `/citizen` dialog peel — `GlassCitizenDialogRequest` → `citizen-dialog-request-LATEST.json` (journal-only, no human→PF Publish); habitat bridge 0.5.496 replies as `Citizen · citizen @PF → @PM`
- 2026-08-02: Intercom operator default **Света** (not Who); `kind=who`/`origin=who` no longer map to operator/human — Who = Agent Who series. Glass LatchPaint parity. cdp-mcp **0.5.494**
- 2026-08-02: Glass chord deepen — Ctrl+K `fd/at/op/mb/ms` + Ctrl+Q FDS/attach/open/build/tests palette entries (still ≠ full Avalonia CascadeChord)
- 2026-08-02: ADR0128 Glass thin attach chips — `GlassAttachChipPeel` body/`attachments` · feed chip buttons · `/attach` composer bracket; tests 4/4 (≠ full Avalonia Skia)
- 2026-08-02: FDS shelf flesh — plan/shared/report/pressure pulses on `GlassFdsGlance` · `/fds` · CabinGlass `fds` → FlightDataStorage; cdp-mcp **0.5.491**
- 2026-08-02: P1 EICAS assembled stack (alert+qrh+ecl) + severity color · FDS M-page skeleton `FlightDataStorage` (`GlassFdsGlance` ≠ FDR)
- 2026-08-02: Glass P0b — Topics empty hint + `/open path[:line]` thin attach↔code peel (not full ADR 0128)
- 2026-08-02: Intercom identity name+kind (guest/citizen/operator) — latch+journal+Glass RoleLabel; defaults Кир/guest · Who/operator; cdp-mcp **0.5.490**
- 2026-08-02: dig lived Glass UX gaps for standalone 15.08 — matrix above; north star ≠ wire-citizen alone
- 2026-08-02: agent surface parity v0 Sense layout — `cdp_glass`/`surface_desk` + surface-cmd/reply latches + WPF `GlassUiLayoutSnapshot`/`GlassSurfaceCommandHub`; dogfood windows=3 (main,pfd_host,mfd_host) named controls; contract `agent-surface-parity-contract-v0.md`
- 2026-08-01: invent dig Glass 0-sync after residual latch triad — GAP none; dual-cockpit projector↔LatchHub matrix green; epic presence DoD CLOSED

- 2026-08-01: act Glass `disk-LATEST` peel — `CdpHabitatPaths.DiskLatchFileName` · `LatchHub.DiskChanged` · `LatchPaint.PaintDisk` · `MainWindow.DiskSurface` → AvalonEdit reload + human Save publish; UIA ` · disk`
- 2026-08-01: act Glass `shared-LATEST` peel — `CdpHabitatPaths.SharedLatchFileName` · `LatchHub.SharedChanged` · `LatchPaint.PaintShared` · `MainWindow.SharedSurface` → EditorPathLabel ` · shared`; UIA dogfood LatchHub.cs
- 2026-08-01: act Glass `land-LATEST` peel — `CdpHabitatPaths.LandLatchFileName` · `LatchHub.LandChanged` · `LatchPaint.PaintLand` · `MainWindow.LandSurface` → AvalonEdit open/goto; dogfood latch goto L32
- 2026-08-01: invent dig Glass 0-sync residual after SoftOrgan chrome — GAP triad `land`/`shared`/`disk` (Avalonia projectors exist; Glass LatchHub silent); next = land peel
- 2026-08-01: act Glass `seats-LATEST` peel — `CdpHabitatPaths.SeatsLatchFileName` · `LatchHub.SeatsChanged` · `LatchPaint.PaintSeats` · `MainWindow.SeatsSurface` → SelectMfdPage + SoftOrganBand `cabin`; dogfood Terminal MFD + cabin chrome; SoftOrgan chrome GAP closed
- 2026-08-01: invent dig SoftOrgan chrome parity beyond MFD — latch SoftOrgans DONE; Ps1/MdAuthor DIG REJECT invent SoftOrgan latch; GAP = Glass missing `seats-LATEST` cabin chrome (Avalonia has CdpSeatsProjector)
- 2026-08-01: invent verify close — Glass 0-sync MFD presence DoD CLOSED (matrix SoftOrgan|peel|reject|hold + SemanticMap DIG REJECT enum invent); next invent = SoftOrgan chrome parity beyond MFD
- 2026-08-01: dig reject invent `MfdShellPage.SemanticMap` — Glass/CDP CabinGlass string `SemanticMap` is arch SoftOrgan projector alias + latch glance; Avalonia graph stays PFD `WorkspaceNavigationMapView` (RelatedFiles = MFD list only); PageOrder/allowance have no SemanticMap; do not add enum that Avalonia MFD shell does not host
- 2026-08-01: invent dig Glass 0-sync MfdShellPage DoD matrix — SoftOrgan|peel|reject|hold stamped above; orphan peels closed; presence DoD CLOSED; HOLD stubs WebAiPortal/Correspondence intentional; SemanticMap Glass-only vs enum noted
- 2026-08-01: invent verify close — Glass orphan MFD peels (WH/EnvReady/Events/Hypotheses) closed: wire in `MainWindow.MfdBody` + SoftOrgan unbound (tests); glance suite 28/28; no `CIDE orphan` stubs remain; SoftOrgan invent still forbidden
- 2026-08-01: act Glass orphan MFD presence peels Events/Hypotheses — `GlassEventsGlance` (cdp *-LATEST + DataBus catalog) + `GlassHypothesesGlance` (debug-hypotheses.json counts); live dogfood Events·READY + Hypotheses·MISSING; SoftOrgan unbound; orphan MFD set closed
- 2026-08-01: act Glass orphan MFD presence peels — `GlassWorkspaceHealthGlance` (READY/THIN/MISSING · root/git/sln/.cascade-ide) + `GlassEnvironmentReadinessGlance` (AGENT_NOTES_FILE / NETCOREDBG_PATH / dotnet PATH); live dogfood WH·READY + EnvReady·READY; SoftOrgan still unbound
- 2026-08-01: act Glass Git status Process-redirect TextBox thin peel — `GlassRedirectedGit` + `MainWindow.GitSurface` (`git status -sb` + `log -5 --oneline`); live dogfood `redirected · status -sb`; Avalonia GitPanel SSOT
- 2026-08-01: act Glass HybridIndex FS status glance peel — `GlassHybridIndexGlance` (MISSING/READY + db path/size); live dogfood HybridIndex · MISSING; SoftOrgan still unbound; Avalonia HCI SSOT
- 2026-08-01: dig reject hold Glass Git MFD — SSOT Avalonia `GitMfdPageView`/`GitPanel`; SoftOrganMfdGlance unbound (no SoftOrganKind); Glass MFD stub only; do not fork status/diff/commit/submodule UI into TextBlock; optional next = Process-redirect `git status` thin peel (Terminal/Build class)
- 2026-08-01: dig reject hold Glass DebugStack DAP — SSOT Avalonia `IdeDapDebugSession` + `DebugStackMfdPageView`; SoftOrganMfdGlance ←`debug_desk` + `CdpDebugDeskProjector` chrome; do not fork DAP protocol/session into TextBlock (unlike Terminal/Build/Tests Process-redirect peels)
- 2026-08-01: act Glass Tests redirected log TextBox thin peel — `GlassRedirectedTest` + `MainWindow.TestSurface` (`dotnet test` Process redirect, prefer CascadeIDE.Tests + SoftOrganMfdGlance filter); live MFD dogfood `redirected · CascadeIDE.Tests.csproj`; SoftOrganMfdGlance test_desk footnote `■ Glass redirected log TextBox` / `□ Avalonia TestsMfdPageView`; Avalonia test host remains SSOT
- 2026-08-01: act Glass Build redirected log TextBox thin peel — `GlassRedirectedBuild` + `MainWindow.BuildSurface` (`dotnet build` Process redirect, ANSI strip); live MFD dogfood `redirected · CascadeIDE.sln` + restore stream; SoftOrganMfdGlance toolchain footnote `■ Glass redirected log TextBox` / `□ Avalonia BuildMfdPageView`; Avalonia MSBuild host remains SSOT
- 2026-08-01: act Glass Terminal redirected TextBox thin peel — `GlassRedirectedShell` + `MainWindow.TerminalSurface` (Process redirect, ANSI strip, no ConPTY); live MFD dogfood `redirected · pwsh` + prompt; SoftOrganMfdGlance Terminal footnote `■ Glass redirected TextBox` / `□ Avalonia ConPTY SSOT`; ConPTY remains Avalonia-only
- 2026-08-01: dig reject hold Glass Terminal ConPTY — SSOT Avalonia `TerminalMfdPageView`/`IntegratedShellLaunch`; do not fork ConPTY into TextBlock; intentional peel = redirected TextBox (shipped)
- 2026-08-01: live `cdp_intercom op=presence` after hard self remount — Cursor seat was on stale `D:\cdp-mcp` (`unknown_op`); terminal_* hard → 0.5.373; latch PF busy / PM composing; partner_for_glass=`@PF · busy` (host currently undocked `M · MFD` — IntercomSubtitle not in that window)
- 2026-08-01: invent dig-queue drain verify — SoftOrgan peel DoD closed (MFD glance or chrome); orphan MFDs unbound; no open SoftOrgan invent leaf; next = act peels / dogfood holds · glass card
- 2026-08-01: dig close Glass Editor AvalonEdit when Forward=intercom — peel already ADR 0120 (`MountEditor(MfdEditorHost)`); drop misleading stub; body clears when chrome on M · cascade-ide `ce66a661`
- 2026-08-01: dogfood quiet Dark Cockpit + compact SoftOrgan host card — live MFD host Terminal: quiet flat chrome, `┌ host ┐` □Glass/■Avalonia, no festive accents · %TEMP%\glass-dogfood-compact.png
- 2026-08-01: SoftOrganMfdGlance compact card — metrics chip row; host footnote box; drop stamped noise · cascade-ide `0c342585`
- 2026-08-01: dig reject SoftOrgan Chat MFD bind — primary = Forward Intercom; MFD Chat = secondary presence card (`FormatChatMfdGlance` PF/PM latch) · cascade-ide `a3d6fbec`
- 2026-08-01: Glass Dark Cockpit palette (no P/F/M rainbow) — `GlassDarkCockpit.xaml` + quiet zone chrome; MFD stubs/glances concise+graphic (`□ Glass` / `■ Avalonia`); SoftOrganMfdGlance footnotes shortened · cascade-ide (ship)
- 2026-08-01: DIG REJECT verify close — Glass MarkdownPreview (`dd34a63a`), WebAiPortal (`171dd52e`), SemanticMap dig bookkeeping; SoftOrganMfdGlance ←`report` for MD; invent dig-peel queue drained
- 2026-08-01: DIG REJECT verify close — Glass MarkdownPreview Markdig host (already `dd34a63a`); SoftOrganMfdGlance ←`report`; Avalonia MarkdigMarkdownPreviewRenderer SSOT; TM dig+shipped closed
- 2026-08-01: Intercom partner presence MLP — latch `intercom-presence-LATEST.json` (not voice/journal; not CockpitHostLatchHydration); `cdp_intercom op=presence seat= state=idle|composing|busy`; Glass IntercomSubtitle merge + PM composing debounce · cdp-mcp + cascade-ide (ship)
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
- Glass reverse: MFD orphans `WorkspaceHealth`/`EnvironmentReadiness`/`Events`/`Hypotheses` — Glass presence peels shipped (FS/env/latch/JSON); Avalonia SSOT; SoftOrgan unbound
- SoftOrgan peel DoD closed: every `SoftOrganKind` resolves (MFD or chrome); orphan MFDs stay unbound — do not invent SoftOrgan to fill CIDE pages; EnvironmentReadiness/IdeHealth are CCUs not SoftOrgans
- Chat/Intercom: primary presence = Glass Forward Intercom feed (latch/journal); MFD `Chat` stays secondary stub — not SoftOrgan bind
- MFD instrument glance: `Build`←`toolchain`, `Terminal`←`sys`, `Tests`←`test_desk`, `DebugStack`←`debug_desk`, `Problems`←`review`, `SemanticMap`←`arch`, `AiChatSettings`←`mcp`, `MarkdownPreview`←`report`, `RelatedFiles`←`refactor` (`SoftOrganMfdGlance`); field-enrich DoD closed for mapped SoftOrgans; Terminal/Build/Tests/Git-status have Glass Process-redirect thin peels (Avalonia ConPTY/MSBuild/Tests hosts remain SSOT); DAP/DebugStack + Git stay Avalonia-only (DIG REJECT below); graph+Problems+RelatedFiles DIG REJECT below; Git SoftOrganMfdGlance unbound
- dig reject: Glass WPF Git MFD instrument host — SSOT = CIDE Avalonia `GitMfdPageView` + `GitPanel` (status/diff/commit/submodule); SoftOrganMfdGlance unbound (no SoftOrganKind — do not invent); Glass stays CabinGlass stub (do not fork panel into TextBlock; optional later Process-redirect `git status` thin peel ≠ full Git MFD)
- dig reject: Glass WPF DebugStack DAP — SSOT = CIDE Avalonia `IdeDapDebugSession` + `DebugStackMfdPageView`; SoftOrganMfdGlance ←`debug_desk` + `CdpDebugDeskProjector` quiet chrome; Glass stays latch glance + CabinGlass stub (do not fork DAP protocol/session/stack into TextBlock; Process-redirect peels ≠ DAP)
- dig reject: Glass WPF Terminal ConPTY — SSOT = CIDE Avalonia `TerminalMfdPageView`/`IntegratedShellLaunch`; Glass ConPTY fork rejected; redirected TextBox peel shipped separately
- dig reject: Glass WPF Build MSBuild — SSOT = CIDE Avalonia `BuildMfdPageView`/`BuildOutputPanelViewModel`; Glass stays `toolchain` latch glance until WPF build-log host peel · cascade-ide `f2a6cf10`/`51655639`
- dig reject: Glass HybridIndex SoftOrgan glance — CabinGlass `hybrid_index`/`hci`/`codebase_index` → MFD only; no SoftOrganKind; live HCI SSOT = Avalonia `HybridIndexMfdPageView`/`HybridIndexOrchestrator` (do not invent SoftOrgan) · cascade-ide `31c962a9` · Glass FS status glance shipped (`GlassHybridIndexGlance`) — presence ≠ SoftOrgan invent
- 2026-08-01: DebugStack←`debug_desk` SoftOrganMfdGlance + `CideDebugDeskLatch` · cascade-ide `ad2e9b56` · cdp-mcp `ab90576`
- 2026-08-01: Avalonia `CdpDebugDeskProjector` quiet chrome from `debug_desk-LATEST` · cascade-ide `ff29fca7`
- invariant: Glass SoftOrgan MFD glance = dual-HCI peel DoD for instrument *presence*; live Terminal/Build/Tests hosts stay on CIDE Avalonia (do not duplicate orchestration into Glass TextBlock)
- SolutionExplorer: Glass `.sln` project-list glance + flat WPF TreeView peel (`GlassSolutionExplorerGlance` / `MfdSolutionExplorerTree`); full tree SSOT = Avalonia `SolutionExplorerView` (do not fork nested file tree into Glass) · cascade-ide `fb1092ad`
- 2026-08-01: Tests←`test_desk` SoftOrganMfdGlance + `CideTestDeskLatch` · cascade-ide `061f607f` · cdp-mcp `6f25247`
- 2026-08-01: Avalonia `CdpTestDeskProjector` quiet chrome from `test_desk-LATEST` · cascade-ide `e01a5c86`
- 2026-08-01: SoftOrganMfdGlance Problems/SemanticMap field enrich · cascade-ide `313c650d`
- 2026-08-01: SoftOrganMfdGlance mcp/report field enrich · cascade-ide `8598e179`
- 2026-08-01: SoftOrganMfdGlance RelatedFiles/refactor field enrich · cascade-ide `2d0190c2`
- dig reject: Glass SolutionExplorer SoftOrganMfdGlance ← `files_desk` — SoftOrganKind.FilesDesk exists (FM utility ADR-0016); CabinGlass pin → SE; Glass `.sln` TreeView/glance is instrument peel (do not overlay FM latch on SE body) · cascade-ide `2e9b86c8`
- 2026-08-01: SoftOrgan `files_desk` latch + Avalonia `CdpFilesDeskProjector` quiet chrome · cdp-mcp `aab96f7` · cascade-ide `3aba5ef7` (CabinGlass chrome_hint `agent · M: files`; SoftOrganMfdGlance still unbound for SE)
- dig reject: SoftOrganMfdGlance RelatedFiles ← `find_desk` — SoftOrganKind.FindDesk DoD via CabinGlass pin→RelatedFiles + chrome_hint `agent · M: find`; SoftOrganMfdGlance stays ←`refactor` (1:1 MFD map; search ≠ debt/blast) · cascade-ide `d8354f02`
- 2026-08-01: SoftOrgan `find_desk` latch + Avalonia `CdpFindDeskProjector` quiet chrome · cdp-mcp `7eb365d` · cascade-ide `10911745` (CabinGlass chrome_hint `agent · M: find`; SoftOrganMfdGlance stays ←refactor)
- dig reject: SoftOrgan crs/Correspondence latch+chrome invent — CabinGlass pin correspondence/crs → MFD Correspondence only (chrome_hint null); no SoftOrganKind for CRS; SoftOrganKind.Crm chrome stays await/callout (must NOT map to Correspondence); SoftOrganMfdGlance Correspondence unbound; live CRS SSOT = Avalonia + `cdp_analysis_scene` feature=correspondence (do not invent SoftOrgan) · cascade-ide `ec7c0b82` · cdp-mcp `6beb3e4`
- dig reject: Glass WPF Correspondence CRS instrument host — SSOT = CIDE Avalonia `CorrespondenceMfdPageView` + `WorkspaceNavigationMapViewModel.Correspondence` (ADR 0155/0156); Glass stays CabinGlass MFD pin stub until WPF CRS host peel (do not fork Avalonia doc↔code into TextBlock) · cascade-ide `560ff86b`
- dig reject: Glass WPF MarkdownPreview Markdig instrument host — SSOT = CIDE Avalonia `MarkdigMarkdownPreviewRenderer` + `MarkdownPreviewToolViewModel`/`MarkdownPreviewWindow`; SoftOrganMfdGlance ←`report` stays; Glass stays latch glance + CabinGlass stub (do not fork Markdig Control into TextBlock) · cascade-ide `dd34a63a`
- dig reject: Glass WPF WebAiPortal WebView instrument host — SSOT = CIDE Avalonia `WebAiPortalMfdPageView` + `Features/WebAiPortal` (ADR 0108 bridge); SoftOrganMfdGlance unbound; Glass stays CabinGlass stub (do not fork portal into TextBlock; CDP `cdp_browser` ≠ this MFD) · cascade-ide `171dd52e`
- dig reject: Glass WPF SemanticMap Skia graph instrument host — SSOT = CIDE Avalonia `WorkspaceNavigationMapView` + `WorkspaceNavigationMapViewModel` (ADR 0039/0053/0056; HCI orientation ADR 0113 ≠ SM graph); SoftOrganMfdGlance ←`arch` stays; Glass stays latch glance + CabinGlass stub (do not fork Skia graph into TextBlock; HybridIndex MFD ≠ this page) · cascade-ide `6d6e1f54`
- dig reject: Glass WPF Problems Roslyn list instrument host — SSOT = CIDE Avalonia `ProblemsMfdPageView` + `ProblemsPanelViewModel`; SoftOrganMfdGlance ←`review` stays; Glass stays latch glance + CabinGlass stub (do not fork diagnostics ListBox into TextBlock; `sa_desk` chrome ≠ Problems MFD / EICAS) · cascade-ide `3aab0e6c`
- dig reject: Glass WPF RelatedFiles related/find_usages instrument host — SSOT = CIDE Avalonia `RelatedFilesMfdPageView` + `WorkspaceNavigationMapViewModel`; SoftOrganMfdGlance ←`refactor` stays (find_desk pin ≠ displace); Glass stays latch glance + CabinGlass stub (do not fork related list/Skia into TextBlock) · cascade-ide `4fd14b57`
- operator glass: `[cockpit_host] exe` = WPF `CDP.GlassCockpit.Windows` (not Avalonia `CascadeIDE.exe`); Intercom Forward feed is ops voice — Avalonia Chat panel is not the operator console

