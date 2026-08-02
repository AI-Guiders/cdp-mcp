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
