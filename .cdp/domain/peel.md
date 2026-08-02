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

- 2026-08-02 → **0.5.457**: batch FileLines near-miss — CitizenCompletions.Anthropic+Finish · IdeToolCallWatch.Helpers · IdeToolchainChannel.Ops.
- 2026-08-01 → **0.5.443**: thin Meta shipped; live preview dogfood OK (`HandleAsync` → `IdePeelChannel.Dispatch.cs`).
