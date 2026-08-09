# Domain card: Glass 0-sync (dual-HCI)

- id: `glass`
- organs: WPF `CDP.GlassCockpit.Windows` · Avalonia projectors · CDP latches
- product: `#CIDE` / `#CDP`
- learn: `note-0-sync-glass-entity-parity` — every CDP desk entity needs CIDE/Glass presence (stub OK)

## Invariants

- Topology compose inside `(…)`: `+` = spatial **split** (both visible); `/` = **OneOf** XOR (full TopLevel, role switches). Example `(P/M)(F)` ≠ `(P+M)(F)`. OneOf DoD = chord **and** auto-switch. Canon: cascade-ide `docs/design/topology-oneof-slash-v0.md`. **Shipped:** parser+flags+Glass `pm_oneof_host` · chord `po` · auto M←MFD/seats · P←plan latch.
- CDP habitat = SSOT; Glass = projector (ADR-0021 Windows-first WPF).
- Quiet SoftOrgan seats republish = chrome tip only; `show_face` (PlaceOrgan / Citizen go) = human attention — BringCabin + SelectMfd when mfd_page, Prefer P when face_seat=p.
- Quiet `land-LATEST` default (`show_face=false`) = Agent-Side tip only; Face AvalonEdit + PreferSurface only on land/presentation invite (`show_face=true` / Command:show). Disk reload of open editor is quiet (no SelectMfd steal).
- Face invite page SSOT = cascade-ide `GlassFacePagePolicy` (path kind → MFD page; explicit `mfd_page` override wins). Land latch stays path+`show_face` — no MainWindow extension ifs. Document Face (`Editor`|`MarkdownPreview`) PreferSurface token = `m` (not world).
- Sticky `web_ai_url` may survive non-browser PlaceOrgan; Glass `RunWebAiPortal` only when Face/MFD targets browser (`SeatsWebAiNavigateGate` / `WantsWebAiNavigate`) — not on every `show_face`.
- SoftOrgan chrome band ≠ EICAS: alert/qrh/ecl stay EICAS (not SoftOrganLatchCatalog).
- `sa_desk` SoftOrgan → quiet chrome (`sa-desk-LATEST` / WorkspaceChromeBand) — not MFD `Problems`, not EICAS `go=sa`.
- Quiet-chrome SoftOrgans (dedicated latch/projector): `sa_desk`, `crm`, `plugins`, `webcam` — presence = WorkspaceChromeBand, not force-MFD.
- Seat/chrome SoftOrgans: `plan` (P), `pressure` (L1), `sys` (legacy pulse; banner/board already carry slim status).
- SoftOrganLatchCatalog gates SoftOrgan `*-LATEST.json`; LatchHub routes alert/qrh/ecl separately.
- CabinGlassProjectionCatalog: every SoftOrganKind go-pin resolves (MfdPage or chrome_hint stub).
- Host start hydration (`CockpitHostLatchHydration`) must include SoftOrgan + EICAS latch names that exist on disk.
- Intercom partner presence = separate latch `intercom-presence-LATEST.json` (idle|composing|busy + reader stale) — do **not** mix into voice/journal, SoftOrgan, EICAS, or host-start hydration (fake freshness).
- Intercom sticky Who = separate latch `intercom-identity-LATEST.json` (freeform nick per seat) — resolve explicit `name=` → sticky → bootstrap (`Кир`/`Operator`/`Citizen`); machine-local nick (e.g. Света) is claim, not repo default. `cdp_intercom op=identity`.
- Intercom Face rails = journal `channel` tag (`crew`|`radio`|`dm`); `cdp_intercom send channel=` (omit→radio). Glass feed filters by active rail. MCP dialog jsonl ≠ Face Intercom.
- `mcp` SoftOrgan → MFD `AiChatSettings` (MCP settings live there); MFD `Chat` = Intercom/citizen secondary — not mcp.

## Entry

- WPF UiKit: `CDP.GlassCockpit.Windows/UiKit` · `GlassSoftKeyBar` · `GlassDeckCard` · tokens in `GlassDarkCockpit.xaml` (unified modern language / edit-locus — not SoftFL invent, not Avalonia ECAM clone)
- WPF: `LatchHub` · `MainWindow.IntercomHud` (Korry AUTOI/HILD/VAD + HDG/CRS) · `EicasBandAggregator` · `LatchPaint` (seats+land+shared+disk+ignite-wake) · `MainWindow.SeatsSurface` · `MainWindow.LandSurface` · `MainWindow.SharedSurface` · `MainWindow.DiskSurface` · `MainWindow.IgniteWakeSurface`
- Avalonia: `CdpEclProjector` · alert/qrh projectors
- CDP: `Cide*Latch` · `CabinGlassProjectionCatalog` · `CockpitHostLatchHydration`

## Antipatterns

- **Dual Input (Airbus sidestick)** — conflict on the **same** PreferSurface / `mfd_page` stick (both shove → wipe). Distinct from **desired dual-HCI**: Agent-Side and Human-Side may sit at different SSOT loci without 1:1 hard couple; `show`/`show_face` = invite, not automatic drag. **SoftFL b1+b3 SHIPPED 2026-08-09:** quiet land default + presentation agent SelectMfd gated on `show_face` (human origin still may switch); topology still applies. Residual gap = true dual viewport / independent PreferSurface sticks (wave b4 later) — not stamp as fully closed habitat.
- Fixing WebView2 airspace with WPF `Popup`/`AllowsTransparency` for cabin overlays — Popup HWND floats above other apps (GitHub/browser); park WebView2/VT instead.
- Sticky `web_ai_url` + any `show_face` → `RunWebAiPortal` (Sierra message / find Face steals browser) — incomplete dual-mode SoftFL; gate navigate on browser Face/MFD only.
- Stuffing alert/qrh into SoftOrgan band (tests explicitly ignore as EICAS bleed).
- Mapping `sa_desk` → MFD `Problems` (gates pulse paints WorkspaceChromeBand; Problems = quality/review family).
- Mapping `crm` → MFD `Correspondence` (CRM = await/callout chrome; CRS = doc↔code surface).
- Inventing SoftOrganKinds to bind orphan MFD pages (Events/Hypotheses/WorkspaceHealth/EnvironmentReadiness) — presence ≠ invent entity.
- SoftOrganMfdGlance remapping RelatedFiles ← find_desk (stays ←refactor; FindDesk Face = own MFD + glance ←find_desk SoftFL unpin).
- Inventing SoftOrganKind for crs/Correspondence latch+chrome (CabinGlass MFD pin only; Crm chrome ≠ CRS; SoftOrganMfdGlance stays unbound).
- Soft deploy ≠ remounted habitat; Glass WPF rebuild is separate from cdp-mcp seat.
- Treat `cockpit_host · down` as process death without checking Debug vs Release path_orphans — false-down + twin Start is the bug; refuse twin, path= orphan or kill then preferred.
- Spawn Glass as CdpMcp child (direct Process.Start) — seat reclaim / KillRunning `entireProcessTree` quiet-kills cabin on remount; cabin-family Start must detach (`cmd /c start` + FindByExePath).
- Intercom MarkdownBody as ContentControl setting Content during DataTemplate expand — double-parents built tree (cabin crash); use StackPanel+deferred rebuild.
- Treat Glass Ctrl+Q `c:` as GlassChord-only aliases — SSOT is `IntentMelody/intent-catalog.toml` via `GlassIntentMelodyCatalog`; chords stay on Ctrl+K.
- Mapping `mcp` SoftOrgan → MFD `Chat` (Chat = Intercom/citizen; MCP settings = AiChatSettings).
- Festive per-zone accents (cyan P / gold F / purple M) — Dark Cockpit violation; geography by label, color only on deviation (ON GND / select / EICAS).
- Autoi wake charge as Intercom chat bubble / compact chip — SoftOrgan tip + StatusText + `ignite-wake-LATEST` is the wake panel; **Intercom HUD** paints AUTOI/HILD Korry + HDG/CRS from `ignite-LATEST` (not chat). Glass filters Autoi from feed (`GlassAutoiWakeFeed`).
- Dump `@intent` / `@event` / peer `ack=` wire into human Intercom letter — strip at publish (`CitizenIntercomHumanSurface`) + paint (`CompactIntercomBody`); hands = «Сделала: …».
- **enforce `raw_diff_as_primary`:** ship Git/Glass UI with raw unified diff as human primary → soft-refuse until human face (files/hunks/tint). Dump legal ⇒ think optional.
- **enforce `glass_ship_without_human_shot`:** `#CIDE` `done`/`shipped` needs `evidence=path.png` **on disk** (`IdeHumanFaceShield`); `shot=true` bool alone refused (operator «Выстрела нет» 2026-08-04); `force=true` escape only.
- **enforce `wave_ship_without_teeth`:** `wave shipped` refuses pending items (no auto-complete rectangle) + human-faced waves need PNG + fresh `domain=` stamp (`IdeWaveShipShield` / `IdeSeemingDoneShield`); `feature_done` on #CIDE/human-faced feature same teeth.
- **enforce `wrong_window_shot_as_done`:** PrintWindow / webcam of `F · Intercom` (or wrong seat) while claiming MFD chips Done = seeming; shoot `title=M · MFD host` (or correct seat) and **Read PNG into chat**.
- **enforce `shot_seen_as_closed`:** Closed = **Page Ready to Interact/use**. Shot + `cdp_see` alone ≠ Closed (HCI HybridIndex glance 2026-08-06).
- **enforce `role_blast_twin_as_done`:** Editor ROLE that lists the same companion names as BLAST (or mini-Skia clone under Editor) = seeming; ROLE glance = orphan|IN-MAP · Nn/Ee · map on MFD; hop map stays SemanticMap MFD.
- **enforce `autoi_as_chat`:** Autoi wake as Intercom chat — already filtered; do not re-teach via chat.
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
| HybridIndex | unbound (no SoftOrgan invent) | `GlassHybridIndexGlance` + live probe + **search/reindex hand** | READY-to-Interact |
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

## PreCondition (operator SSOT — restore 2026-08-05)

**Glass Done human-flight ≠ substitute for:** `All CIDE surfaces adopted` on Glass — **CIDE/Avalonia cabin is EOL** for the standalone path.

**2026-08-08 PreCondition REOPENED (throw-Cursor honesty):** prior CLOSE stamped a **narrower** human-flight slice (Adopt inventory + SoftOrgan Face + PNG) while operator DoD max = **standalone CDP without Cursor**. Peer sit-internet Face gap (PlaceOrgan browser → WebView2 navigate; default chatgpt landmine) = seeming Done.

**Battle VERIFY lived 2026-08-08** (Sierra `@intent browser` HN + Face WebAiPortal PNG + DDG search default) — checklist PASS; **Glass Done stamp still refused** vs max DoD standalone-without-Cursor (not half-a rebrand of battle VERIFY as epic Done). SoftOrgan Meta invent stays REJECT.

What closed under the name Glass Done was a **narrower DoD** (presence / SoftOrgan / latch peels / P0–P3 lived UX slices — e.g. Topics **P0b** = strip+gap, not ADR 0072 overview). That is **half-a vs operator PreCondition** when a needed CIDE surface still only lives on Avalonia or as stub/strip.

**Operator clarify 2026-08-05:** PreCondition **was** stamped; failure mode = **half-a instead of A** (did not finish), not «forgot PreCondition». Being axis: playbook-being-vs-seeming § Seeming-Done. Sibling seat owns inventory+port; do not invent competing leaf from discussion seat.

- **Do not** re-stamp Glass Done while needed Intercom/CIDE surfaces remain unadopted (topic overview 0072, spine/summary 0096, …) unless operator explicitly shrinks PreCondition.
- **Adopt** = lived Glass WPF face (or explicit DIG REJECT + named external SSOT) — not agent-only, not File.Exists, not Avalonia-still-primary.
- NorthStar messenger (`#crew`/DM/Radio) may **supersede** some CIDE Intercom chrome — supersede must be **named**, not silent undership of overview.

### PreCondition gap inventory (2026-08-05 dig — operator: **not only Intercom Overview**)

Scope = **all** CIDE surfaces still Avalonia-primary / half-a on Glass. Presence/peel CLOSED ≠ Adopted. Port **ready mechanics** only (no invent). NorthStar supersede must be named per row.

#### A. Intercom / Chat (Avalonia Skia still mechanic SSOT)

| # | Surface | CIDE ready SSOT | Glass now | Verdict |
|---|---------|-----------------|-----------|---------|
| A1 | **0072** overview ↔ detail ↔ back + adaptive default | `ChatTopicOverviewPolicy` · Skia `CardPanel`/`OverviewHeader` · `ato`/`atb` | Glass WPF card grid + strip + Back · adaptive · `topic_overview`/`topic_enter` | **ADOPTED** 2026-08-05 lived (`tmp-glass-shots/topic-overview-a1-20260805.png`) |
| A2 | **0096** topic-card **summary** + product **spine** | `ChatThreadOverviewPresentation` · `SpineStrip` · `ChatProductSpine*` | Glass richer Summary + `ProductSpineStrip` latch | **ADOPTED** 2026-08-05 lived (`tmp-glass-shots/topic-overview-a2-20260805.png`) |
| A3 | **0172** worklines / session-graph (`ThreadNode`) | Avalonia compositor + tree | 30m quiet-gap clusters only | **SUPERSEDE** — NorthStar `#crew`·DM·Radio index; not ThreadNode port (see glass-intercom antipattern) |
| A4 | Message↔code depth | Skia attach/reveal fuller | `/open` + chips · `/intercom message find` + `anchors` (2026-08-06 denser thin · live GREEN · wave CLOSED) | **thin CLOSED** — relate/event-log Avalonia SSOT (SoftFL REJECT invent full peel) |
| A5 | Topic intents parity (`enter`/`return` overview) | Avalonia melody `ato`/`atb` | Glass melody `topic_overview`/`topic_enter` + `ato`/`atb` · next/prev in overview selects | **ADOPTED** 2026-08-05 (with A1; surface enter↔overview OK) |
| A6 | **0136** message select (gutter · slash · ПКМ · MCP) | Avalonia Skia gutter + `/intercom message select` + `chat_select_*` | Glass: select N·N:M·`[a;b]…` · next/prev · ПКМ · melody `chat_select_*`→ordinal · gutter+highlight · Enter-autocomplete waits for N (not bare usage) · attach aliases + topic/spine slash · find/relate/anchors honest refuse | **ADOPTED denser** 2026-08-06 · A6 slash residual CLOSED (honest) · SoftFL REJECT invent IntercomCodeRef · A4 message↔code denser still open |

#### B. Instruments — presence DONE, Avalonia still denser

| # | Surface | CIDE denser SSOT | Glass now | Verdict |
|---|---------|------------------|-----------|---------|
| B1 | IdeDap full DAP drive UI | `IdeDapDebugSession` | latch stack/locals + Continue/Step bridge | **DIG REJECT / defer** — host densest-for-Glass; full drive UI ≠ 1-hop |
| B2 | WNM / PFD navigation map full | `WorkspaceNavigationMapView` | `GlassSemanticMap` Skia multi-hop | **DIG REJECT / defer** — SemanticMap already densest peel |
| B3 | Full Intent Melody catalog discoverability | Avalonia full catalog | Ctrl+Q `c:` allowlist peel | **DIG REJECT / defer** — allowlist spray ≠ full catalog invent |
| B4 | CascadeChord AwaitMelodyTail | Avalonia | Glass Ctrl+K overlay (residual wave CLOSED) | **CLOSED densest** |

#### C. Already adopted / CLOSED (not PreCondition gaps)

MFD presence · SoftOrgan chrome · land/shared/disk · Terminal VT · WebAi WebView2 · Markdown FlowDocument · CRS full · Problems Roslyn · Git porcelain depth · most densest overnight peels — see matrices above.

#### D. DIG REJECT / deferred (do not invent)

| Item | Note |
|------|------|
| SoftOrgan for Ps1Desk/MdAuthor | presence = Terminal / MarkdownPreview MFD |
| Design polish / graphic language | operator: hosts first, design later |
| `#humans`/`#agents` rooms | NorthStar reject — lens 0143 |
| Session-graph day-1 suffering | glass-intercom antipattern — prefer named supersede over blind port of A3 |

#### Port wave (a×b — ready first)

1. **A1+A5** — **ADOPTED** 2026-08-05 · Glass WPF overview/detail/back + adaptive (`ChatTopicOverviewPolicy` in GlassCore) · melody `ato`/`atb` · evidence `cascade-ide/tmp-glass-shots/topic-overview-a1-20260805.png`. Keep Glass 30m cluster ids until A3 decided.
2. **A2** — **ADOPTED** 2026-08-05 · richer card Summary (`GlassTopicCardSummary`) + product spine strip (`ChatProductSpine*`→GlassCore + `GlassProductSpineStore`) · evidence `topic-overview-a2-20260805.png`.
3. **A3** — **SUPERSEDE** 2026-08-05 · no ThreadNode/session-graph port; channel index = NorthStar (`#crew`·DM·Radio); 30m clusters = Virtual History only.
4. **B1→B3** — **DIG REJECT / defer** 2026-08-05 · no ready ≤1-hop denser peel; hosts already densest-for-Glass.
5. **A4** — denser thin find/anchors shipped+lived 2026-08-06 (`071433f8`); residual = relate/event-log SoftFL REJECT.

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
| Intercom identity | sticky Who latch + RoleLabel · bootstrap `Кир`/`Operator`/`Citizen` · Who ≠ operator · personal nick via `op=identity` | **P0 shipped** |
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

- **2026-08-09 Face busy hold ttl=0 (CDP 0.5.695)** — lived SoftFL Glass Done residual: Autoi Radio tips interleaved with Sierra #crew while Face Turn >120s (presence stale → mute miss). Ship habitat presence hold until finally idle. SoftFL invent ACCEPT dig=glass.md+citizen.md. SoftOrgan invent REJECT. Not Glass Done.
- **2026-08-09 Mentions SoftFL · @PF Face≻tip (cascade-ide)** — lived tip≠Face: `@PF` → Cursor while Face citizen in `pf_profiles`. Ship GlassCore Face roster + `TryCitizenFace` · tests **23/23**. SoftFL invent ACCEPT densify dig=glass-intercom.md. SoftOrgan invent REJECT. Residual Mentions invent UX alone.
- **2026-08-09 Face Ship gate + remount mute (0.5.693 / CDP)** — take walls no longer dump into Radio; remount Autoi Radio muted while Face Who busy. Mentions SoftFL alone.
- **2026-08-09 Face files Ship listing (0.5.692 / CDP)** — human Radio saw thin Hands ok pulse after `files`; SoftOrgan HND chips ≠ journal. CDP ships entries as Applied.Ship + Face append (no SoftOrgan viz invent). Mentions SoftFL alone.
- **2026-08-09 Intercom Identity SSOT Kit** — Glass `GlassIntercomIdentity.Claim` sealed (CDP sole writer). Axes Seat/Who/Kind/Origin/Sink; guest≠FM model. Mentions SoftFL alone.
- **2026-08-09 SoftOrgan HandsReceipt Kit (HND chips)** — catalog `hands` · ShortLabel HND · priority 3 · ChipLevel RUNNING→Caution · latch `hands-LATEST.json` from CDP. Letter laundry REJECT (Face SoftOrgan band owns receipt). Cascade SoftOrgan density tests green. Mentions SoftFL alone.

- **2026-08-09 GlassFacePagePolicy SSOT (Face path→page — not situational .md if)** — `GlassFacePagePolicy.Resolve` in GlassCore · OpenCodeFile Face invite calls it · PreferSurface `MarkdownPreview`→`m` (was world) · tests GlassFacePagePolicy+PresentationPmOneOfPolicy **26/26** · live `M · MarkdownPreview` land show note-citizen-persona… · evidence `cascade-ide/tmp-glass-shots/face-md-preview-policy-20260809.png` + `cdp_see`. SoftFL invent ACCEPT densify · SoftOrgan invent REJECT · not Glass Done.

- **2026-08-09 SoftFL dual-HCI land/presentation quiet (b1+b3 — not Glass Done)** — nested[axb] FULL-A: `land-LATEST` `show_face` default false · Glass LandSurface tip-only · `OpenCodeFile(showFace)` PreferSurface gate · Disk reload quiet · presentation `PresentationMaySelectMfd(origin,show_face,mfd)` · CDP `NavigationLand`/`CidePresentationLatch` show_face · Avalonia land/presentation projectors parity · tests NavigationLandLatch **3/3** · PresentationPmOneOfPolicy **16/16**. Residual b4 = true dual viewport. SoftFL invent ACCEPT densify · SoftOrgan invent REJECT · Sierra densify `@all` not stolen (AllMention regex unblock only).

- **2026-08-08 SoftFL ACCEPT densify ×3 Intercom/@Kir dup + topology Dialog + @ SlashPopup (Done REOPENED)** — dig=Света SoftFL. Cascade: FanOut `_seen` + `@` Suggest on SlashPopup. CDP: Dialog `presentation_set` + InventedHands desk layout recover. SoftFL invent ACCEPT densify · SoftOrgan invent REJECT · Face HOLD.

- **2026-08-08 Dig SoftFL invent REJECT densest SoftFL ACCEPT after dual-SSOT wave CLOSED (Done REOPENED)** — dig=`glass.md`+`citizen.md`+wave shipped. SoftFL ACCEPT NuGet/flat/CamelCase/PlaceOrgan CLOSED · A4 relate SoftFL invent REJECT · B1–B3 DIG REJECT · densest sealed next = Face axis4 operator HOLD (not SoftFL invent mill) · SoftFL invent REJECT SoftOrgan invent REJECT · not Glass/Citizen Done.
- **2026-08-08 CitizenBattleReady15 dual-SSOT wave SHIPPED (Done REOPENED)** — dig=`active-wave.json` w-20260807-213307 lag close: i1 dual-ssot-png already done · i2 kir-face-tip CLOSED dig=`glass.md` SoftFL @Kir Face Radio tip + evidence `D:\cdp-mcp\tmp-evidence\kir-face-tip-radio-20260808.png` + `cdp_see` · i3 park-invent CLOSED dig=dual-SSOT stamp «invent SoftFL NEXT thrash parked» + evidence `D:\cdp-mcp\tmp-evidence\dual-ssot-plan-20260808.png` + `cdp_see`. SoftFL invent REJECT SoftOrgan invent REJECT Face axis4 HOLD · not Glass/Citizen Done · SoftFL invent ACCEPT SoftFL only as wave lag close (lived≠pending).

- **2026-08-08 SoftFL ACCEPT nuget.org flat-container 0.1.3 index VERIFY (Done REOPENED)** — dig=`https://api.nuget.org/v3-flatcontainer/aiguiders.hybridcodebaseindex.core/index.json` versions now include **0.1.3** (restore-ready; gallery lag closed). SoftFL invent ACCEPT · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done · dig=flat API.
- **2026-08-08 Dig densest FullReady peer residual SoftFL invent REJECT CLOSED (Done REOPENED)** — dig=`citizen.md` Monday DoD shell SoftFL CLOSED + throw-Cursor SoftFL residual SoftFL invent REJECT CLOSED + Face axis4 HOLD. SoftFL invent REJECT Dig REJECT mill · SoftOrgan invent REJECT · not Glass/Citizen Done.
- **2026-08-08 Dig Plan leaf-board text-hell SoftFL invent REJECT CLOSED (Done REOPENED)** — dig=`cascade-ide/.cdp/domain/softorgan-human-viz.md` last_ship Plan leaf-board SoftFL **2026-08-07** already CLOSED + evidence `plan-leaf-board-instrument-20260807.png`. SoftFL invent REJECT SoftOrgan invent REJECT SoftFL invent REJECT Kill-text-hell SoftOrgan invent REJECT · not Glass/Citizen Done.
- **2026-08-08 SoftFL ACCEPT nuget.org HybridIndex Core 0.1.3 gallery VERIFY (Done REOPENED)** — dig=`https://www.nuget.org/packages/AIGuiders.HybridCodebaseIndex.Core/0.1.3` PAGE200 · versions **0.1.3** 8/8/2026 · CamelCase/FTS densify README section · download 71.26 KB · flat-container still lag («not indexed yet» for restore) · PackageReference already 0.1.3 · ProjectRef primary lived. SoftFL invent ACCEPT · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done · evidence `D:\cdp-mcp\tmp-evidence\softfl-safe-dig-plan-20260808.png`.
- **2026-08-08 Dig SoftFL-safe densest residual SoftFL invent REJECT CLOSED (Done REOPENED)** — dig=`glass.md`+`citizen.md`+nuget.org `…/0.1.3` (PAGE 200 · versions table **0.1.3** 8/8/2026 · CamelCase/FTS densify section · flat-container lag «not indexed yet»). SoftFL-safe densest = SoftFL ACCEPT nuget.org **0.1.3 gallery VERIFY** residual. Plan leaf-board SoftFL invent REJECT → SoftFL already CLOSED 2026-08-07. SoftFL invent REJECT · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done · evidence `D:\cdp-mcp\tmp-evidence\softfl-safe-dig-plan-20260808.png`.
- **2026-08-08 SoftFL ACCEPT HybridIndex Core NuGet 0.1.3 publish + PackageReference VERIFY (Done REOPENED)** — dig=`nuget.org`+`GlassCore.csproj`+CamelCase wave pkg residual. Tag `v0.1.3` → Trusted Publishing workflow **success** · nuget.org `AIGuiders.HybridCodebaseIndex.Core` **0.1.3** · Glass/Cascade PackageReference fallback `0.1.2→0.1.3` · ProjectRef path still primary lived. SoftFL invent ACCEPT · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done.
- **2026-08-08 Dig SoftFL-safe densest residual SoftFL invent REJECT CLOSED (Done REOPENED)** — dig=`glass.md`+`citizen.md`+cascade `softorgan-human-viz.md`+nuget.org `AIGuiders.HybridCodebaseIndex.Core` (0.1.3 missing)+live PrintWindow. Named SoftFL ACCEPT CamelCase/HybridIndex/PlaceOrgan CLOSED; PreCondition A1–A6/B densest CLOSED|SUPERSEDE|DIG REJECT invent peel; Plan leaf-board SoftFL already CLOSED 2026-08-07. SoftFL-safe densest residual = SoftFL ACCEPT HybridIndex Core **NuGet 0.1.3** publish + PackageReference fallback (CamelCase wave pkg; ProjectRef already lived). Live: `M · HybridIndex` `hci · search · 11 hits · BoardLeaf · ws=cascade-ide` · evidence `D:\cdp-mcp\tmp-evidence\softfl-safe-dig-plan-20260808.png` + `cdp_see`. SoftFL invent REJECT · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done.
- **2026-08-08 SoftFL ACCEPT HybridIndex CamelCase/FTS densify live dogfood VERIFY (Done REOPENED)** — dig=`glass.md`+`GlassHybridIndexStatusProbe.cs`+SQLite FTS5 unicode61 (no native CamelCase split). Ship hybrid-codebase-index-core **0.1.3** `FtsCamelCase` ExpandBody+BuildMatchTerm · tests **10/10** · GlassCore ProjectRef path fix `..\\..\\hybrid-codebase-index-core` (was always NuGet miss). Live: Prefer M · HybridIndex · SoftKey reindex · search `BoardLeaf` → `hci · search · 11 hits · BoardLeaf · ws=cascade-ide` · Face `M · HybridIndex` · hits `PlanBoardLeaf.cs` · evidence `cascade-ide/tmp-glass-shots/hybridindex-camelcase-boardleaf-20260808.png` + `cdp_see` · dig=FtsCamelCase.cs · SoftFL invent ACCEPT · SoftOrgan invent REJECT · not Glass/Citizen Done · Face axis4 HOLD.
- **2026-08-08 SoftFL ACCEPT PlaceOrgan default quiet Face only on show VERIFY (not Glass Done)** — dig=operator densify after 0.5.682: default showFace:true still hijacked Glass on every soft pin. Ship cdp-mcp **0.5.683** default quiet · intentional Face only on browser/Meta show. Live: face HN → quiet domain+webcam → Glass still **M · WebAiPortal** HN · evidence `cascade-ide/tmp-glass-shots/softfl-placeorgan-default-quiet-face-hn-20260808.png` + `cdp_see`. SoftFL invent ACCEPT densify · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done.
- **2026-08-08 SoftFL ACCEPT PlaceOrgan showFace densify Face VERIFY (not Glass Done)** — dig=lived Face steal: plan/webcam PlaceOrgan(showFace:true) while sticky HN → face_seat away from browser → SeatsWebAiNavigateGate false / SelectMfd Plan. Ship cdp-mcp **0.5.682** quiet plan/webcam. Live after quiet webcam: Glass still **M · WebAiPortal** `https://news.ycombinator.com/` HN list · evidence `cascade-ide/tmp-glass-shots/softfl-browser-show-face-hn-after-quiet-webcam-20260808.png` + `cdp_see`. SoftFL invent ACCEPT densify · SoftOrgan invent REJECT · Face axis4 HOLD · not Glass/Citizen Done.
- **2026-08-08 Dig throw-Cursor SoftFL residual SoftFL invent REJECT CLOSED (Done REOPENED)** — dig=`glass.md`+`citizen.md`+live Glass shot. SoftFL ACCEPT FULL-A residuals CLOSED; SoftFL invent REJECT (no ≤1-hop invent under seal). Live PrintWindow `CDP GlassCockpit · Windows` evidence `cascade-ide/tmp-glass-shots/softfl-face-hn-glass-20260808.png` + host-Read: PLAN seat · WHY Glass Done · NEXT Peer residual · COURSE Citizen Done · leaf board FLY0/OPEN5/DONE25 · OPEN includes Face residual axis4 / Intercom Face Slack — SoftOrgan/Face HOLD ≠ SoftFL invent mill. Shot ≠ WebAiPortal HN → refuse Face HN VERIFY stamp. SoftOrgan invent REJECT · not Glass/Citizen Done.
- **2026-08-08 SoftFL ACCEPT Glass HybridIndex search hits densify live dogfood VERIFY (Done REOPENED)** — dig: prior SoftFL VERIFY 0-hits was fixture/`tmp-glass-shots` scope + stale index (PlanBoardLeaf post-dates 2026-08-05); MCP reindex cascade-ide → hits; Glass SoftFL densify paints `ws=` on HCI status so fixture vs repo scope is Face-honest (do **not** pass `.sln` into HybridIndex Core — separate DB key). Live: `/solution load CascadeIDE.sln` → Prefer M · HybridIndex · SoftKeys search → `hci · search · 5 hits · PlanBoardLeaf · ws=cascade-ide` · HCI READY · DOCS 4563 · scope map hub `cascade-ide` · hit list `PlanBoardLeaf.cs`/`MainWindow.PlanLeafBoard.cs`/… · evidence `cascade-ide/tmp-glass-shots/hybridindex-search-hits-20260808.png` + host-Read · dig=`MainWindow.HybridIndexSurface.cs` · SoftFL invent ACCEPT · SoftOrgan invent REJECT · not Glass/Citizen Done · Face axis4 remains operator HOLD.
- **2026-08-08 SoftFL ACCEPT Glass HybridIndex search/reindex hand live dogfood VERIFY (Done REOPENED)** — Ctrl+Q `MFD: HybridIndex` → Prefer M · HybridIndex Face · SoftKeys search/reindex/refresh · HCI READY cards (DOCS/FRESH/STATE/DB) · query `PlanBoardLeaf` → status `hci · search · 0 hits · PlanBoardLeaf` (honest empty) · reindex SoftKey densified map (`_softfl-test-fail` node) · evidence `cascade-ide/tmp-glass-shots/hybridindex-search-hand-20260808.png` + `cdp_see` · dig=`MainWindow.HybridIndexSurface.cs` · SoftFL invent ACCEPT (named READY-to-Interact densify residual) · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 Dig SoftFL-safe densest residual after SoftFL ACCEPT VERIFY complete → DIG REJECT SoftFL invent (Done REOPENED)** — dig=`glass.md`+live P·Plan SoftFL instruments (WHY/NEXT/COURSE + leaf board FLY/OPEN/DONE + human leaf titles) · named SoftFL ACCEPT VERIFY FULL-A residuals CLOSED (SoftKeys·FindDesk·/search·SE filter·Build fail·Tests fail) · SoftOrgan Meta invent REJECT · A4 relate/B1–B3 SoftFL invent REJECT · Plan leaf SoftFL already shipped 2026-08-07 (live VERIFY SoftFL not text-hell invent gap) · densest sealed next ≠ SoftFL invent / SoftOrgan invent · Face axis4 = operator HOLD · SoftFL-safe next = SoftFL ACCEPT HybridIndex search/reindex hand live dogfood VERIFY (READY-to-Interact densify) · evidence `cascade-ide/tmp-glass-shots/dig-softflsafe-residual-20260808-1101.png` + `cdp_see` · SoftFL invent REJECT · not Glass/Citizen Done stamp.
- **2026-08-08 SoftFL ACCEPT Glass Tests fail DoubleClick jump live dogfood VERIFY (Done REOPENED)** — Soft chord `ms` → M · Tests · redirected `dotnet test` SoftFlTestFail.csproj · 1 fail · DoubleClick → M · Editor open `BrokenTests.cs` line 10 · status `glass · tests · SoftFlTestFail.BrokenTests.FailsOnPurpose` · densify `GlassTestOutputParse` (keep stack body after empty Error Message/Stack Trace headers; match `:line N`; stop Message before Failed!/summary) · evidence `cascade-ide/tmp-glass-shots/tests-fail-dblclick-jump-20260808.png` + `cdp_see` · dig=`MainWindow.TestSurface.cs` + `GlassTestOutputParse.cs` · SoftFL invent ACCEPT (named Tests fail jump residual of FULL-A) · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 SoftFL ACCEPT Glass Build fail DoubleClick jump live dogfood VERIFY (Done REOPENED)** — Soft chord `mb` → M · Build · redirected build SoftFlBuildFail.csproj · 2 err · DoubleClick `Broken.cs(7,16) CS1525` → M · Editor open `Broken.cs` · status `glass · build · error Broken.cs(7,16) CS1525` · evidence `cascade-ide/tmp-glass-shots/build-fail-dblclick-jump-20260808.png` + `cdp_see` · dig=`MainWindow.BuildSurface.cs` · SoftFL invent ACCEPT (named Build fail jump residual of FULL-A) · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 SoftFL ACCEPT Glass SE filter TextBox live dogfood VERIFY (Done REOPENED)** — `/solution explorer show` → M · SolutionExplorer Face · filter TextBox `MainWindow` (AutomationId `MfdSolutionExplorerFilter`) · tree filtered to MainWindow*.cs (GlassCore HostSurface + GlassCockpit.Windows partials) · tooltip Filter solution tree · evidence `cascade-ide/tmp-glass-shots/se-filter-textbox-20260808.png` + `cdp_see` · SoftFL invent ACCEPT (named SE filter residual of FULL-A) · dig=`MainWindow.SolutionExplorerSurface.cs` · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 SoftFL ACCEPT Glass /search slash live dogfood VERIFY (Done REOPENED)** — Radio Composer ValuePattern `/search FindDeskSurface` → Send → M · FindDesk Face · status `find · FindDeskSurface · 2 · workspace` · slash bubble `glass · slash · /search` · hits `_dogfood-search-slash.ps1:33/62` · evidence `cascade-ide/tmp-glass-shots/search-slash-finddesk-20260808.png` + `cdp_see` · SoftFL invent ACCEPT (named /search residual of FULL-A) · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 throw-Cursor battle dogfood VERIFY (Done REOPENED)** — Autoi wake sealed course: SoftOrgan Meta CLOSED (A14 invent refused) · flew named battle checklist. Step0 Dialog: Sierra `@intent browser open url=HN` (not «нет браузера») · lynx peer ack · Face `m=browser` `web_ai_url=https://news.ycombinator.com/` · live **M · WebAiPortal** WebView2 HN (Voyager/Nixpkgs…) · evidence `cascade-ide/tmp-glass-shots/battle-webai-hn-face-20260808-0945.png` + `cdp_see` · search default DDG HTML · Dig REJECT SoftOrgan Meta invent · Glass/Citizen Done stay **REOPENED** vs max DoD (battle lived ≠ epic Done stamp).
- **2026-08-08 FindDesk latch hits SoftFL (Done REOPENED)** — overnight anti-waste: `CideFindDeskLatch` now writes `hits[]` (path/line/preview, cap 80) from `IdeFindChannel` so Sierra `@intent find` / `cdp_search` paints Glass FindDesk list without `/search` alone. Tests `CideFindDeskLatchTests` + CabinGlass pin. Prior FULL-A wave still stands; **not** Glass/Citizen Done.
- **2026-08-08 SoftFL ACCEPT FindDesk Face live dogfood VERIFY (Done REOPENED)** — Soft chord `find` → M · FindDesk Face · latch `find · ResolveRg · 1 · project` · hit `FindInFilesTests.cs:45 ResolveRg_finds_habitat_or_path` · evidence `cascade-ide/tmp-glass-shots/finddesk-face-20260808.png` + `cdp_see` · SoftFL invent ACCEPT (named FindDesk dogfood residual of FULL-A) · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 SoftFL ACCEPT FilesDesk SoftKeys live dogfood VERIFY (Done REOPENED)** — Soft chord `files` → M · FilesDesk Face · UIA SoftKey `Up` Invoke · SoftKeys Up/Open/List live on Face · evidence `cascade-ide/tmp-glass-shots/filesdesk-softkeys-up-20260808.png` + `cdp_see` · SoftFL invent ACCEPT (named SoftKeys dogfood residual of FULL-A) · not Glass/Citizen Done · SoftOrgan invent REJECT.
- **2026-08-08 SoftFL FULL-A port wave SHIPPED (Done REOPENED)** — FilesDesk SoftKeys Up/Open/List + DoubleClick/Enter local cd · FindDesk Face + CabinGlass `find_desk→FindDesk` unpin (RelatedFiles stays refactor) · `/search` paints FindDesk · ADR0125 slash family `/file open|pick|save` `/solution open|load|new|explorer show` `/folder open` · SE filter TextBox · Build/Tests fail DoubleClick jump · Peel17 WorkspaceFileIndex/McpSolutionTree/SolutionExplorerTreeFilter linked+Compile Remove · unit: GlassSlashCatalogStepTests + CabinGlassProjectionCatalogTests · **not** Glass/Citizen Done · SoftFL invent REJECT invent SoftOrgans/Themes/Intercom-relate/full IdeDap · open A14–A22 SoftOrgan cards / throw-Cursor battle.
- **2026-08-08 sticky web_ai show_face SoftFL (not Glass Done)** — operator «опять недоделка»: Sierra message opened browser while sticky HN URL lived. Ship: `SeatsWebAiNavigateGate` · `LatchPaint.SeatsView.WantsWebAiNavigate` · `SeatsSurface` RunWebAiPortal only when wants · tests LatchPaintSeatsWebAiNavigateTests **3/3**. SoftFL invent ACCEPT · Done REOPENED.
- **2026-08-08 WebView2 airspace SoftFL (not Glass Done)** — operator: palette under HN; Popup SoftFL floated over GitHub/Yandex (rejected). Ship: keep in-tree overlays; park `WebView2`/`TerminalVt` while palette/chord/open-family open (`MainWindow.Airspace`). Lived: surface `palette` open=true · evidence `cascade-ide/tmp-glass-shots/airspace-palette-park-20260808.png` (Command palette over dark MFD, URL bar HN). SoftFL invent ACCEPT · Done REOPENED.
- **2026-08-08 Editor Face systemic** — dig=operator Ctrl+Q MFD:Editor peel / stuck F·Intercom · `GlassEditorFace` MountEditor(M) · never FormatMfdStub · PreferSurfaceFromMfdPage Editor→`m` + PreferPmOneOf AlignActiveSurfaceToZone · evidence `cascade-ide/tmp-glass-shots/mfd-editor-face-20260808.png` (M·Editor + AvalonEdit) + `cdp_see` · cascade `f19c0422`/`4866857d`/`00a89320` (push `github` HTTPS). SoftFL invent REJECT.
- **2026-08-08 SE Face ItemsSource systemic** — dig=operator Avalonia peel shot · Glass SE = `SolutionItem` TreeView ItemsSource (auto-load workspace `.sln`) · never `FormatMfdStub` Avalonia peel on SE · cabin `Glass.DarkListItem`/`DarkTreeItem` · evidence `cascade-ide/tmp-glass-shots/se-face-tree-clean-20260808.png` + `cdp_see`. SoftFL invent REJECT.
- **2026-08-08 Dig densest Citizen Done DoD SoftFL invent REJECT CLOSED → Citizen Done REOPENED (throw-Cursor)** — prior stamp was **seeming** vs max DoD `standalone CDP without Cursor`: peer sit-internet Face missing (lynx text ≠ WebView2 sit). SoftFL invent REJECT invent residual still holds. Evidence PNG proves dialog+vision Full Ready slice only — **not** throw-Cursor Done.
- **2026-08-08 Glass Done PreCondition REOPENED (throw-Cursor)** — prior STAMPED was half-a vs operator north star throw Cursor; reopen until sit-internet Face wired + lived dogfood. Dig REJECT rows / SoftOrgan Face still valid lived inventory — do not re-claim epic Done.
- **2026-08-08 sit-internet Face SoftFL SHIPPED (throw-Cursor residual — not epic Done)** — seats `web_ai_url` → Glass `RunWebAiPortal` · default URL duckduckgo · Citizen `@intent browser` + agent `cdp_browser` PlaceOrgan · SeemingDoneShield refuse throw-Cursor Done without `…webai….png` · live `M · WebAiPortal` DDG evidence `cascade-ide/tmp-glass-shots/webai-sit-internet-20260808.png`. SoftFL invent REJECT · Glass/Citizen Done stay REOPENED.
- **2026-08-08 Dig densest Citizen Done residual SoftFL invent REJECT CLOSED** — dig=`glass.md` PreCondition +`citizen.md`+plan `glass-cide-port-glance` · ALL SURFACES ADOPTED Done (Themes Dig REJECT · A4 relate Dig REJECT Avalonia SSOT) · SoftOrgan Face Done SHIPPED · SoftFL invent REJECT ≤1-hop · densest sealed next = **Glass Done PreCondition stamp** (human-flight PNG + named Dig REJECT rows — not DIG REJECT mill alone). Evidence Soft:QRH `softorgan-qrh-face-cards-20260808.png` · cascade `9944c9bd`.
- **2026-08-08 Dig SoftFL-safe residual expand CLOSED** — dig=`glass.md`+`citizen.md`+`glass-intercom.md` · SoftFL invent REJECT (A4 relate/event-log · SoftOrgan Meta · MarkdownPreview invent) · no ≤1-hop SoftFL invent under seal · SoftFL-safe lived: cascade-ide `origin` push SSH `193.124.113.7:22` timeout → `git push github develop` HTTPS OK (`db8fd77a`) · densest sealed next = SoftOrgan human viz Face Done **axis4 operator eyes** (not DIG REJECT mill as Glass Done) · Face PrintWindow title=`CDP GlassCockpit · Windows` · Plan WHY/NEXT/COURSE + Soft:QRH palette · evidence `cascade-ide/tmp-glass-shots/dig-softflsafe-residual-20260808.png` + `cdp_see`. SoftFL invent REJECT.
- **2026-08-08 Sierra sit-internet HN Face VERIFY (not Glass Done)** — Dialog→`@intent browser`→`web_ai_url` HN → Glass `M · WebAiPortal` WebView2 lived · PrintWindow evidence `cascade-ide/tmp-glass-shots/webai-sierra-hn-face-20260808.png` + `cdp_see` · SoftFL seats preserve URL across webcam shot (`a636f34`) · Done stays REOPENED vs throw-Cursor max DoD.
- **2026-08-08 SoftFL @Kir Face Radio tip VERIFY** — lived under Composer Stop: tip published AutoI → IsNoise StatusText-only → `IsKirVoiceCannonFaceTip` allowlist (cascade-ide `db8fd77a`, push SSH pending) · Face Radio ChatBubble · evidence `cascade-ide/tmp-glass-shots/kir-face-tip-radio-visible-20260808.png` + `cdp_see` · Voice Letter #198. SoftFL invent REJECT.
- **2026-08-08 dual-SSOT Plan Face VERIFY** — live PrintWindow title=`CDP GlassCockpit · Windows` · WHY=`Glass Done — instruments people can fly` · NEXT=`CitizenBattleReady15 dual-SSOT verify + dogfood PNG` · COURSE=`Citizen Done toward 15.08` · evidence `cascade-ide/tmp-glass-shots/dual-ssot-plan-20260808-0033.png` + `cdp_see` · invent SoftFL NEXT thrash parked · SoftFL invent REJECT. Kir Face tip residual CLOSED same night.
- **2026-08-07 Intercom cannon → Composer not habitat** — lived: `@Kir mention check` claimed (`85ef57e9681f` in cannon-fired) but `ignite-wake` channel=habitat (Sierra duplex busy stole prefer). Fix: `MayPreferHabitatOverComposer` excludes `intercom-pf-*`. External guest wake stays CDT. SoftFL REJECT invent.
- **2026-08-07 mention axes Seat+Kind+Who → WakeSink** — `@PF`/`@PM` seats · `@guest`/`@citizen`/`@operator` · Who (`@Sierra`/`@Света`/`@Kir`) · wake=f(kind): guest→external cannon (Cursor AutoI), citizen|operator→Glass Face · seat follows occupant kind (Sierra·citizen `@PF` → Face not Cursor) · `GlassIntercomMention.ResolveWakes` · tests **20/20**. SoftFL REJECT invent.
- **2026-08-07 @PM seat mention + Who cues** — `@PF`/`@PM` = seats (meta-roles); Who sticky for cues (`@PM→Света`); Face BringCabinAttention on @PM; SoftFL REJECT person-tag invent.
- **2026-08-07 @PF mention any lane → PF wake** — messenger semantics: CIT/HOST/`/citizen` keep lane message; `GlassIntercomMention` + `TryNotifyPf` writes human→PF latch (no journal/share dup) for AutoI cannon. Tests GlassIntercomMention **11/11**. SoftFL REJECT invent full mention UX.
- **2026-08-07 ShowFace PlaceOrgan attention SoftFL-safe** — Place≠attention: `PlaceOrgan`/`Citizen` go → seats `show_face`+`face_seat` (quiet `TryPlaceExplicit` no). Glass BringCabinAttention + SelectMfd(mfd) or Prefer P. Face seat projects alone (no sibling MFD steal). Tests CideSeatsLatch **6/6** · PresentationPmOneOfPolicy **12/12**. Dual hard `build_utc=2026-08-07T08:16:47Z` · Glass restart. Dogfood latch show_face → Face `P · Plan` + `M · Git` PNG `tmp-glass-shots/showface-git-live-20260807.png` (+ force Plan). SoftFL invent stubs DIG REJECT. Autonomy OFF.
- **2026-08-07 webcam iconic Face SoftOrgan path VERIFY** — overnight Face residual: cabin up pid=37464 minimized → webcam Enum dropped (tiny iconic rect) so SoftOrgan/#CIDE shot protocol could not list `CDP GlassCockpit · Windows`. SoftFL-safe webcam ship (placement+hwnd+Opt) dual hard; lived maximize PrintWindow + `cdp_see` evidence `cascade-ide/tmp-glass-shots/sat-eve-webcam-iconic-face-20260807-0057.png`. **Not** Glass Done claim — Face axis4 remains operator eyes for SoftOrgan human viz Done.
- **2026-08-06 Dig densest ≤1-hop Cursor-cliff DIG REJECT** — dig glass+citizen+ops last_ship: SoftFirst/Citizen residual/FullReady SoftFL STRUCK/Kill text-hell/peer-journal/PathMutate duplex already CLOSED·SHIPPED · B1–B3 DIG REJECT · Face axis4=operator · residual=SoftFL invent or human page. No ≤1-hop Cursor-cliff under SoftFL invent REJECT. Evidence `cursorcliff-1hop-digreject-20260806-1635.png` + `cdp_see`. SoftFL REJECT.
- **2026-08-06 Dig Flight durable SoftFirst DIG REJECT** — SoftFirst/flight-durable already VERIFY CLOSED (ops+glass 2026-08-05/06); cabin pid=37464 survived SoftFirst remounts · densest NEXT ≠ SoftFirst invent · Face axis4=operator. Evidence `softfirst-flight-digreject-20260806-1630.png` + `cdp_see`. SoftFL REJECT.
- **2026-08-06 Dig Citizen Done residual DIG REJECT** — dig citizen.md+glass.md+glass-intercom: habitat peer chain SHIPPED · PreCondition A ADOPTED/SUPERSEDE/thin CLOSED · A4 denser SoftFL REJECT · B1–B3 DIG REJECT/defer · SoftOrgan Plan CLOSED · Face axis4=operator. Evidence `cascade-ide/tmp-glass-shots/citizen-residual-digreject-20260806-1628.png` title=`CDP GlassCockpit · Windows` F·Intercom `#crew` AUTOI·HILD·HDG/CRS Glass Done + `cdp_see`. SoftFL REJECT.
- **2026-08-06 FullReady densest SoftFL STRUCK DIG REJECT** — Autoi wake tried densest toward Standalone; domain already SHIPPED `w-20260806-100246` · SoftFL invent REJECT · SoftOrgan Plan WHY+NEXT CLOSED · Face live title=`CDP GlassCockpit · Windows` F·Intercom `#crew` AUTOI·HILD·GLM-5.1·HDG/CRS Glass Done · dig=glass.md last_ship Standalone+Kill-texthell · evidence `cascade-ide/tmp-glass-shots/fullready-digreject-20260806-1609.png` + `cdp_see` · SoftFL REJECT.
- **2026-08-06 Kill text-hell SoftFL DIG REJECT** — dig-lived: Face title=`CDP GlassCockpit · Windows` F·Intercom messenger (human #crew prose, not wire dump) · `CompactIntercomBody` + Intercom prose residual CLOSED · SoftOrgan Plan WHY+NEXT CLOSED · PreCondition A1–A6 ADOPTED/CLOSED (A4 residual=relate SoftFL REJECT invent) · invent SoftOrgan chrome / SoftFL reopen REJECT · preference which wall densify = operator gate · evidence `cascade-ide/tmp-glass-shots/kill-texthell-dig-20260806-1605.png` + `cdp_see` · SoftFL REJECT.
- **2026-08-06 Operator comfort freeze+soak VERIFY** — cabin **up pid=37464** dual-cockpit after Recover `-Seat cdp` remount survive · Citizen dialog «Жива… soak держит» · Face PrintWindow title=`CDP GlassCockpit · Windows` AUTOI·HILD·`(F/P/M)`·GLM-5.1·HDG/CRS Glass Done · evidence `cascade-ide/tmp-glass-shots/comfort-freeze-soak-20260806-1554.png` + `cdp_see` · SoftFL invent REJECT · densest NEXT=Flight durable SoftFirst / Kill text-hell needs human page.
- **2026-08-06 FullReady-standalone-runs DIG REJECT** — Autoi successor wave tried Standalone re-prove; domain already SHIPPED `w-20260806-100246`. Dig=glass.md+inventory · SoftFL/Meta/Plan WHY+NEXT CLOSED · Face axis4 operator. Evidence `cascade-ide/tmp-glass-shots/standalone-digreject-20260806-1508.png` + `cdp_see`. SoftFL REJECT.
- **2026-08-06 FullReady-peer-journal-face DIG REJECT** — dig: northstar `Open (later ship)` peer journal filter / rich directory (`glass-intercom-northstar-messenger-v0.md` L79) · glass residual after channel= Face rail = **later SoftFL**, not lived Cursor-cliff gap · citizen journal dedupe already SHIPPED (feed skips `TryJournalFromView` for kind=citizen) · PeerAck durable latch already exists · Face axis4 = operator eyes · SoftFL invent REJECT · evidence dig=northstar+glass.md+IntercomFeed.cs · Face status PNG `cascade-ide/tmp-glass-shots/peer-journal-digreject-20260806-1400.png` + `cdp_see` (not SoftFL Done claim).
- **2026-08-06 Standalone Runs full-ready wave SHIPPED** — wave `w-20260806-100246` 5/5 · TM leaf **Glass/CDP Standalone Runs · full-ready** · cockpit_host **up pid=37464** dual-cockpit Release · wire **ack=3/3** gen=12 (health+cockpit_host+git status) · evidence `cascade-ide/tmp-glass-shots/standalone-runs-fullready-20260806-1302.png` + `cdp_see` · SoftFL REJECT.
- **2026-08-06 share-glass-axb** — IdeShare `with=operator` mirrors habitat `%LocalAppData%/cdp-mcp/share` + project `.cdp/share` (`WriteOperatorShareFiles`) · tests IdeShare 10/10 · Glass FDS SHARE prefers `share/v1` via `GlassIdeShareGlance` (cascade-ide) · sibling hard deploy
- **2026-08-06 A4 message↔code denser thin wave CLOSED** — find/anchors lived · evidence `cascade-ide/tmp-glass-shots/a4-message-find-anchors-20260806.png` · relate SoftFL REJECT
- **2026-08-06 A4 message↔code denser thin SHIPPED** — `/intercom message find [path:line]` + `anchors` over attach chips · relate stays SoftFL REJECT (Avalonia IntercomCodeRef) · cascade-ide `071433f8` · tests GlassIntercomMessageFindTests 4/4 · live UIA find `1 hit → #81` + anchors · evidence `cascade-ide/tmp-glass-shots/a4-message-find-anchors-20260806.png`
- **2026-08-06 A6 slash residual CLOSED (honest)** — spine show/toggle onto ProductSpineStrip · find/relate/anchors DIG REJECT SoftFL (Avalonia IntercomCodeRef SSOT; A4 denser) · SoftFL REJECT
- **2026-08-06 A6 attach*/topic* slash EXTEND** — Glass `/intercom attach selection|file` · `/intercom attach scope` honest DIG REJECT (melody no false→slash_attach) · `/intercom overview|topic cards|topic open|next|prev` onto existing topic UI · tests GlassPaletteChordCatalog · SoftFL REJECT · residual: find/relate/anchors/spine*
- **2026-08-06 cdp_intercom channel= Face rail SHIPPED** — lived: Kir↔Sierra pain talk via MCP `cdp_citizen` dialog invisible on Face · gap: `Publish(channel=)` existed but `IdeCideIntercomChannel.Send` + MCP schema dropped it (always Radio-default) · wire `channel=crew|radio|dm` (+feed=) through Send/Card/CitizenRouteHost · tests Channel_send_dm + invalid · hard **0.5.674** `build_utc=2026-08-06T06:49:32Z` · live dogfood DM letter to @PM · SoftFL REJECT · residual: Face eyes on DM rail; peer journal filter still later.
- **2026-08-06 Glass slash ADR 0150 ArgTail (unmask)** — park/bare=last was invent. Canon: `arg_tail` none|optional|required · autocomplete Enter auto-runs only when policy allows · bare required → honest usage. SoftFL REJECT.
- **2026-08-06 Claim guest≠demote Sierra** — lived: habitat-map Radio `name=Kir` Claimed tip over citizen · refuse guest Claim when tip is citizen · Sierra restored · SoftFL REJECT.
- **2026-08-06 Glass slash empty-args universal gate** — usage bubble on bare open/citizen/attach/select from autocomplete+palette+Enter. CIDE-parity `ShouldAutoRunOnCommit` · `PrefillComposerForSlashArgs` (no usage dump) · select bare=last · attach fail→park. SoftFL REJECT.
- **2026-08-06 A6 select usage-without-highlight FIX** — Enter on autocomplete ran bare `/intercom message select` → usage bubble, no highlight. Fix: `RequiresArgs` + CommitSlash waits for N · bare `/select`→last · brighter left-bar highlight · ПКМ DataContext `??`/`is` precedence. Tests 7/7 · SoftFL REJECT.
- **2026-08-06 Folded AutoI consume CLOSED** — `GlassIgniteCmdBridge` `autonomous_on` → `Resume`+`SetAutonomous` (0.5.674) · pairs cascade-ide glass-intercom · SoftFL REJECT · residual Glass eyes.
- **2026-08-06 standalone-launch Ready-to-Interact VERIFY** · `cdp_cockpit_host` stop→start cycle · pid=29908 dual-cockpit · latches=32 · Release exe toml · parent≠CdpMcp (detach) · evidence `cascade-ide/tmp-glass-shots/standalone-launch-cycle-20260806.png` + `cdp_see` · SoftFL invent REJECT
- **2026-08-06 ecl-qrh-alert-hand CLOSED** — EICAS SoftKeys `clr`/`ack`/`list` on MFD health band · `eicas-cmd-LATEST.json` → `GlassEicasCmdBridge` → `IdeChkChannel.AckFromGlass` · CLR local suppress until latch pulse changes. Live dogfood: UIA SoftKey `ack` → cmd `done` ship/git-known · ECL open cleared (`ecl · 1 clear`) · shot `tmp-glass-shots/uikit-eicas-softkey-ack-20260806.png` + `cdp_see`. SoftFL invent REJECT · pattern mirror ignite-cmd.
- **2026-08-06 glass-uikit SoftKeyBar+DeckCard CLOSED** — WPF UiKit edit-locus: `GlassSoftKeyBar` + `GlassDeckCard` + SoftKey/Deck tokens in `GlassDarkCockpit.xaml`. HybridIndex hand remounted onto SoftKeys; glance deck via `GlassDeckCard.FromChip`. Live dogfood: UIA SoftKey `search` → `GlassHybridIndexStatusProbe` **9 hits**; DeckCards HCI READY / docs 4308; shot `tmp-glass-shots/uikit-softkey-deck-hybridindex-20260806.png` (title=`M · MFD host`) + `cdp_see`. Cascade tip `318e9476`. SoftFL invent REJECT · not Avalonia ECAM clone.
- **2026-08-06 HCI/HybridIndex Ready-to-Interact CLOSED** — Glass hand: search box + `search`/`reindex`/`refresh` on `MfdHybridIndexHost`; probe `TrySearch`/`TryReindex` (in-proc Core). Live dogfood: UIA set query `GlassHybridIndexStatusProbe` → 9 hits; reindex MISSING/docs0 → READY/docs4308; shot `tmp-glass-shots/hci-hand-after-search-20260806.png`. Cascade tip `8b0af796`. SoftFL REJECT. Avalonia still denser SSOT for ECAM softkeys.
- **2026-08-06 HCI/HybridIndex 4a STRUCK (seeming Closed)** — operator: **Closed = Page Ready to Interact/use**, not «agent saw window». Prior glance-only shot struck; leaf closed by hand above.
- **2026-08-05 flight-durable remount survive VERIFY** — lived SoftFirst dual-seat Not connected → Recover `-Seat cdp` then `cdp-debug` (ops; not SoftFL) · cabin **still up** pid=57780 detach · health 0.5.667 lag=false · window_list → title=`CDP GlassCockpit · Windows` → PrintWindow + `cdp_see` · evidence `cascade-ide/tmp-glass-shots/flight-durable-remount-survive-20260805.png` (AUTOI·HILD lit · HDG/CRS Glass Done (human flight) · #crew · PreCondition banner). SoftFL/Meta REJECT. Hold invent-only stands — dig only next lived residual.
- **2026-08-05 comfort-freeze soak GREEN** — DIG ACCEPT soak verify (not SoftFL invent): cabin survived dual MCP remount (pid=57780 detach) · ICM bound=31 · Citizen dialog reset «Пинг…» → «Жив…» · PNG `cascade-ide/tmp-glass-shots/comfort-freeze-soak-20260805.png` (AUTOI·CFG 0.85·HDG/CRS Glass Done·#crew). Dark/scale DIG REJECT (already LIVE). densest NEXT wave=flight-durable (seat remount thrash lived).
- **2026-08-05 cockpit_host live + ICM dogfood** — dig: inventory SoftFL/Meta CLOSED · sole inv gap=wave idle · real gap=`cockpit_host · down · agent-only` (toml already Glass WPF). `op=start` → pid=57780 dual-cockpit latches=32 · ICM bound aliases=31 · invoke `git_status` ok · PNG `cascade-ide/tmp-glass-shots/cockpit-host-live-icm-20260805.png` (title=`CDP GlassCockpit · Windows` · AUTOI lit · HDG/CRS Glass Done). SoftFL invent REJECT.
- **2026-08-04 file-situ Applies on locus CLOSED** — Glass ECAM APPLIES `En Wm · problems on MFD` + AvalonEdit error/warn line tint (`GlassEditorAppliesLocus` + `GlassEditorAppliesTintRenderer`); situ ribbon Face += Applies; surface stub `file_situ.applies_on_locus` (Glass=SSOT). Live dogfood `_applies-locus-dogfood.cs` → PNG `.cdp/evidence/window-20260804-applies-locus-mfd.png` (M·MFD · APPLIES E2 W0 · red tint on broken line). SoftFL invent REJECT.
- **2026-08-04 file-situ Diff-intent hunk tint CLOSED** — Glass ECAM DIFF `+N −M · Hh` + AvalonEdit green/red hunk tint (`GlassEditorDiffIntent` + `GlassEditorDiffHunkRenderer`); git root = open-file (not cabin WorkspaceRoot — cabin cwd=cdp-mcp was false CLEAN). Surface `file_situ.diff_intent` human summary (not raw dump). PNG `tmp-glass-shots/window-20260804-diff-intent-mfd.png` (M·MFD · situ ribbon · DIFF +23−6·5h · green tint L1–10). SoftFL invent REJECT.
- **2026-08-04 file-situ ROLE glance fix CLOSED** — Operator rejected prior ROLE/GRAPH Done as seeming (companion names twin BLAST). Ship: ROLE = orphan|IN-MAP · Nn/Ee · map on MFD only (no name list); surface `role_in_graph` drops hops[] · `map_locus=semantic_map_mfd` · cascade-ide `ee59b55b` · cdp-mcp `4962047` · PNG `tmp-glass-shots/window-20260804-role-glance-mfd.png` (M·MFD · ROLE IN-MAP ≠ BLAST names). SoftFL invent REJECT. Enforce `role_blast_twin_as_done`.
- **2026-08-04 file-situ Role-in-graph SEEMING (struck)** — was ROLE/GRAPH ECAM listing h1 companion names ≈ BLAST · claimed CLOSED with PNG role-graph-mfd — operator reject being≠seeming.
- **2026-08-04 shared-SSOT file-situ Q2 CLOSED** — Glass ECAM FILE WHY+BLAST under Editor chrome (M·MFD when Intercom owns F) · `GlassEditorSituRibbon.Face/Build` · surface `shared_ssot.file_situ` {path,why_this_file,blast[]} · tests ribbon+IdeGlassSurface · PNG `tmp-glass-shots/window-20260804-file-situ-ecam-mfd.png` (M·MFD · FILE WHY+BLAST lived). SoftFL invent REJECT.
- **2026-08-04 Standalone densest dig (Glass up) CLOSED** — DIG ACCEPT: Q1 WHY/NEXT/COURSE lived on P·PFD + go=surface shared_ssot (Plan latch). DIG REJECT SoftFL/Meta/inventory mill / cabin·ICM·Autoi re-prove (already GREEN). densest next = shared-SSOT file-situ Q2 (WHY-this-file + blast) A×B on Glass+surface. PNG tmp-glass-shots/window-20260804-standalone-dig-pfd-16232.png. Leaf seeded under Standalone feature.
- **2026-08-04 quiet-exit dig CLOSED** — root: pre-detach Glass=`CdpMcp` child → remount `Kill(entireProcessTree)` quiet-exit (no App Error). Detach already shipped (`TrySpawnCabinDetached`). Live prove: Glass pid=16232 Start 15:59:23 outlived both CdpMcp remounts 16:05 (parent gone · AppError today=0 · scene up). Separate kill path Intercom `XamlParseException` logical-child already fixed `5ffc45f2`. Intentional agent Stop-Process for rebuild ≠ quiet-exit. PNG `tmp-glass-shots/window-20260804-cabin-quiet-exit-dig-16232.png` (M·MFD DomainBoard LIVE). SoftFL invent REJECT.
- **2026-08-04 cabin detach survival** — dig: Glass parent=`CdpMcp.exe` → remount `Kill(entireProcessTree)` quiet-exit (no App Error; recur cabin-up #1–#3). Ship: cabin-family `TrySpawnCabinDetached` (`cmd /c start` + FindByExePath); SnapshotLocked OS rediscover cabin-only; stand-in tests stay direct. Dogfood: hard deploy → Start pid=46560 · parent≠CdpMcp PASS · PNG `tmp-glass-shots/window-20260804-cabin-detach-46560.png`. SoftFL invent REJECT.
- **2026-08-04 cabin-up Hold #3** — wake: true `NO GLASS` (not path-orphan) · no Application Error · prior pid quiet-exit. `op=start` → Release pid=43248 latches=32 · PNG `tmp-glass-shots/window-20260804-cabin-up-43248.png` (AUTOI+HILD lit · HDG/CRS · Glass Done · Intercom live). SoftFL invent REJECT. **Quiet-exit dig CLOSED 2026-08-04** (detach survival prove).
- **2026-08-04 human-faced overnight v0** — near-black Dark Cockpit tokens (`#0A` night, G1000 cue) · CFG chip cycles UI scale 0.85/1/1.15/1.3 (`GlassUiScale` + `ScaleRoot`) · Intercom feed strips wire (`CompactIntercomBody`) · publish humanize lives in cdp-mcp 0.5.656. Build Glass green. Eyes dogfood + PNG CLOSED (`window-20260804-032857` + HUD `063729`).
- **2026-08-04 cabin path-orphan fix** — dig: «dies between wakes» ≠ crash (no App Error). Dual Glass Debug+Release; toml preferred=Release; FindByExePath path-strict → false-down + twin Start. Ship: `gui_host=orphan` / `path_orphans` · Start refuse twin (cabin-family only) · dogfood Debug orphan→refuse→kill→Release pid=25892 · PNG `tmp-glass-shots/window-20260804-cabin-path-orphan-fix.png`. SoftFL invent still REJECT.
- **2026-08-04 Intercom HUD v0** — flat Korry AUTOI/HILD/VAD + HDG/CRS from `ignite-LATEST` (`autonomous`/`hild`/`course`); Glass→`ignite-cmd-LATEST` toggle bridge; model picker at Composer. GlassCore `GlassIntercomHud` 3/3 · MCP latch/cmd 4/4 · Glass Release build green. **Eyes dogfood CLOSED** — PrintWindow `tmp-glass-shots/window-20260804-063729-852.png` (AUTOI lit · HDG/CRS · Glass Done course).
- **2026-08-04 cabin-up Hold** — wake dig: `cockpit_host · down · agent-only` (real product gap; SoftFL invent REJECT). `op=start` → pid=31992 latches=32 · PNG `tmp-glass-shots/window-20260804-cabin-up-31992.png` (AUTOI lit · HDG/CRS · Glass Done). Hold continues invent-only.
- **2026-08-04 cabin-up Hold #2** — again `down · agent-only` after remount/Autoi thrash. `op=start` → pid=24088 · PNG `tmp-glass-shots/window-20260804-cabin-up-24088.png` (AUTOI+HILD lit · HDG/CRS). SoftFL invent REJECT. Densest next if recurs: dig why Glass process dies.
- **2026-08-04 Autoi off Intercom feed** — wake charge is SoftOrgan tip / StatusText / ignite-wake latch only; Glass filters Autoi from Intercom journal paint (`GlassAutoiWakeFeed`). Tip `5dd071cf`.
- **2026-08-04 Glass Git tint** — MFD Diff: FlowDocument +/-/@@/meta + add/delete row bg (`GlassGitDiffFlowDocument`); status list Tone (M amber / ?? blue-gray). Verify: `tmp-glass-shots/glass-mfd-diff-highlight-verify.png`. Tip `7479f3c3`. Prior VisualAttentionAxB (P/F/M ignore) stays closed.
- **2026-08-04 VisualAttentionAxB CLOSED** — P plan-LATEST (feature/task/wall, no «TM later») · F Autoi wake compact chip · M gitignore check-ignore (`9 rows · 6 ignored`, not ~599 dll). Webcam verify: `tmp-glass-shots/glass-{pfd,fwd,mfd}-verify.png`. Tips `48714f14`/`d1709107`. Human review = screenshots, not chat diffs.
- **2026-08-04 Voice Letter #157** — Intercom topic IOP lived (surface topic_next); DIG ACCEPT Glass Done leaves closed (board zombie ≠ SoftFL mill).
- **2026-08-04 cabin live** — Rebuild Glass Release (stale exe unknown topic_next); surface `run action=topic_next` → `glass · topic next · …`. Glass Done feature closed. Voice Letter → #157.
- 2026-08-04: Intercom keyboard IOP — melody atn/atp/atb/ato/amn/amp → topic_next/prev/topics_all/slash_open/feed_page; GlassIntercomTopicNav + RunPaletteEntry; tests 21/21 · cascade-ide `c041ad09`
- 2026-08-05: **sticky Intercom Who** — `intercom-identity-LATEST.json` · `CideIntercomIdentityLatch` · `cdp_intercom op=identity` / `send name=` claims · Glass `GlassIntercomIdentity` parity · bootstrap Operator (not Света) · live dogfood send without name= → AutoI · cdp-mcp **0.5.663**
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
- 2026-08-04: Hold DIG — operator «в UI ничего» = `cockpit_host · down · agent-only` (cabin off), not missing organs. Wave CabinUpHumanFlight: `cdp_cockpit_host op=start` → dual-cockpit pid live · ICM bound · surface RPC full debt · Intercom @PM. Antipattern: treat SoftFL/Meta CLOSED + agent tools as Glass Done while GUI host down.
- **0.5.653** — Human-face axe: Autoi `ChargeHumanFacePostfix` + TM `#CIDE` done/shipped refuse without PNG evidence (`IdeHumanFaceShield`) · 2026-08-04
- **2026-08-04** — Glass share-to-model: human Intercom send mirrors IdeShare operator inbox (`GlassOperatorShareShelf` → `.cdp/share` + habitat `cdp-mcp/share`) so `share from=operator` works; PNG evidence Intercom/near-black/CFG
- **2026-08-04 last_ship** — MFD glance chip cards (Events/WH/EnvReady/Hypotheses): `GlassGlanceChip` + `MfdGlanceCardsHost` · cascade-ide `f817032b` · live PNG `.cdp/evidence/glance-*-20260804-103*.png` · design polish deferred = THIS ship (G1000 chip row, not text dump)
- **2026-08-04 last_ship** — FDS + Chat chip cards (wave-2 dump pages): `GlassFdsGlance.Probe` + `BuildFds`/`BuildChat` · cascade-ide `6f869eb9` · PNG `glance-fds/chat-20260804-105*.png`
- **2026-08-04 last_ship** — `glass_scene` cabin SA pulse (`cabin_sa/v0` on `cdp_glass` op=scene + go=`glass_scene`|`cabin_sa`) — agent omnibus of Glass latches without PNG · gap 2.2 CLOSED slice · dig `cascade-ide/scratch/dig-glass-scene-cabin-sa-20260804.md`
- **2026-08-04 last_ship** — FDR hang fix: `surface_desk` Scene latch-fast (skip sync git diff_intent) + `RunGit` kill@3s — was 230s ReadToEnd block

