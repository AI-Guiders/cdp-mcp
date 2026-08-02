# Domain card: Peel (cdp_peel)

- id: `peel`
- organ: `cdp_peel` / `IdePeelChannel` → `roslyn_move_members_to_partial_file`
- product: `#CDP`

## Invariants

- Always ListTools Meta (unlike raw `roslyn_move_members_*` which is act-only shortlist).
- Session `solution_or_project_path` after `cdp_open`; aliases `path`/`members`/`out`.
- `apply=false` default = preview; `true` = TryApplyChanges + DependentUpon (default).
- Decide seam via `cdp_refactor op=partials` first; peel is the cut.

## Entry

- `cdp_peel` / go=`peel`
- Preview: `path=` + `members=` + `out=` (no apply)

## Antipatterns

- Escaping to user-roslyn MCP for routine FileLines peels when habitat=CDP.
- Single-file peels as the epic — batch ~10–15 / one ship; peel organ is hygiene throughput.

## last_ship

- 2026-08-02 → wave23 DIG REJECT — CdpMcp.csproj FileLines=411 open-buffer only (XML; disk scan is `*.cs` → disk ok WARN=0 near=0). refactor_plan `introduce_program_class` is design/cheap=false + mis-shape on .csproj; Program already has Dispatch|Instructions peels (debt=0). Pivot → fix false recommend / citizen chain.
- 2026-08-02 → **0.5.476**: wave22 — extract method_lines HealthJson (75→under warn70) → MetaDispatch.HealthJson.cs helpers (TryExplainTool/TryGetExeBuildUtc/TryReadPendingUpdate). Next: refactor_plan hotspot (CdpMcp.csproj FileLines=411 / design) or next method_lines.
- 2026-08-02 → **0.5.475**: wave21 — extract method_lines FindInFiles.Dispatch (197→thin) → FindInFiles.Dispatch.cs helpers (Fail*/TryBindRoots/BuildRgArgv/OkHits). Search/Rg already peeled. Next: HealthJson=75 soft or next refactor_plan hotspot.
- 2026-08-02 → **0.5.474**: wave20 — extract method_lines MetaDispatch.CoreAsync (267→thin switch) → Core.Man|Capabilities|Context|Open|Restore (+existing Health). Open needs `using Cdp.ScriptableIde` for OpenRecentStore. CoreAsync cleared from refactor_plan debt. Next: FindInFiles.Dispatch=197 · HealthJson=75 soft.
- 2026-08-02 → wave19 DIG REJECT — BuildBudget method_lines already peeled @0.5.452 (Budget.cs=110; BuildBudget~30). Next hotspot: MetaDispatch.CoreAsync=267 (extract_method, not FileLines peel).
- 2026-08-02 → wave18 DIG REJECT — IdeSaChannel FileLines (main=269&lt;350; Handle method_lines cleared @0.5.451; disk ok). Pivot next → method_lines soft-warn.
- 2026-08-02 → **0.5.473**: wave17 — near-miss clear: DeskWireParityTests.Ccu.Catalog · IdeArchBoardChannelTests.AsBuilt.Desk → disk ok (WARN=0 near=0).
- 2026-08-02 → **0.5.472**: wave16 — Tests WARN peels: IdeIgniteArmHostTests.Continuity · IdeArchBoardChannelTests.AsBuilt · IdePluginsChannelTests.OpenVsx · IdeTaskManagerTitlePrecedenceTests.Feature (product Adx/MdInclude/DocEditPlane below floor300 — pivot). disk WARN→0; near left DeskWireParity.Ccu + AsBuilt.
- 2026-08-02 → **0.5.471**: wave15 — IdeProblemsChannel.Parse.Helpers · IdeMdAuthorChannel.Helpers · EditorPlane.Scene.Detail (skip Plugins.Boards — warn=400 / already concern-partial).
- 2026-08-02 → **0.5.470**: wave14 — IdeDeskSeats.Ops · IdeCockpit.Build.PlanPulse.Surface · IdeIgniteArmHost.ContinuityOps.Helpers.
- 2026-08-02 → **0.5.469**: wave13 — IdeToolchainChannel.Recipes.Helpers · IdeCideIntercomChannel.Helpers · IdeFilesChannel.Fs.Helpers (skip IdeSa FileLines — method_lines debt / Decide already peeled).
- 2026-08-02 → **0.5.468**: wave12 — IdeWebcamChannel.Screen.Analyze · IdeArchBoardChannel.AsBuilt.Build · IdeOnboardChannel.Scan.Helpers.
- 2026-08-02 → **0.5.467**: wave11 — EditSniper.Aim · EditorComfort.ClipEdit.Ops · IdeChkChannel.Handle.
- 2026-08-02 → **0.5.466**: wave10 — IdeProblemsChannel.Handle · IdeFilesChannel.Browse · OpenVsxClient.Ops.
- 2026-08-02 → **0.5.465**: wave9 — LspOptionsToolkit.Ops · DocBuffer.Disk · IdeChkChannel.Mutate.Io (skip QrhBuiltins single-method).
- 2026-08-02 → **0.5.464**: wave8 — McpOutletHabitat.Mount · SemanticMap.Hits · IdeSaChannel.Decide.Helpers.
- 2026-08-02 → **0.5.463**: wave7 — DomainPulse.Score · Webcam.Window.Native · Quarantine.Groups.Io.
- 2026-08-02 → **0.5.462**: wave6 — EditPlanFix.Parse · Recommend.Helpers · CdpSettings.Build (partial).
- 2026-08-02 → **0.5.461**: wave5 — Learn Stash/Query/Promote · FDR.Tape · Scope.Ops (skip IdeRepl.Crm single TryCrm).
- 2026-08-02 → **0.5.460**: wave4 — IdeTeethChannel.Ops+Helpers · IdeCockpitHostChannel.Lifecycle · IdeRefactorPlanChannel.Ops.
- 2026-08-02 → **0.5.459**: wave3 — EditSniper.PeekResolve.Wire · IdeFdrThresholdPolicy.Apply · IdeWebcamChannel.Audio.Transcribe (skip MetaDispatch.Core single-method).
- 2026-08-02 → **0.5.458**: wave2 — DeskGoMapCatalog.BuiltIns · EditorPlane.Apply.Validate · IdeOnboardChannel.Persist.
- 2026-08-02 → **0.5.457**: batch FileLines near-miss — CitizenCompletions.Anthropic+Finish · IdeToolCallWatch.Helpers · IdeToolchainChannel.Ops.
- 2026-08-01 → **0.5.443**: thin Meta shipped; live preview dogfood OK (`HandleAsync` → `IdePeelChannel.Dispatch.cs`).
