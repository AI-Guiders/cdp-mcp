# Domain card: Citizen host (cdp_citizen)

- id: `citizen`
- organ: `cdp_citizen` / `IdeCitizenChannel` + `CitizenCompletions` (+ `CitizenCompletions.OpenAiCompat`)
- product: `#CDP`
- ADR: 0026 / 0028 (citizen completions host)

## Invariants

- Live invite needs `open_ai_api_key` **or** `anthropic_api_key` in `%LocalAppData%/CascadeIDE/ai-keys.toml`.
- Prefer **OpenAI-compat** when `open_ai` key set (Cloud.ru FM); else Anthropic.
- Defaults when keys omit URL/model: `https://foundation-models.api.cloud.ru/v1` · `ai-sage/GigaChat3-10B-A1.8B`.
- Wire: Bearer + `{base}/v1/chat/completions` (non-stream for citizen turns); system-as-message on OAI path.
- OpenAI-compat sampling: hardcoded **`temperature=0`** (wire fidelity). Full param map: agent-notes `knowledge/domains/agent-operations/note-llm-sampling-params-openai-compat-v1.md`.
- `invite_ready` is a **record** (not ValueTuple) — JSON must expose Ready/Status/Checklist/Blocker.
- `dry_run=true` builds persona+wire messages without provider; works with empty keys.
- Dry-run **model** label mirrors live `ResolveProvider` (FM-first / `DefaultOpenAiModel`), not raw `DefaultModel` (claude).
- Soft deploy ≠ live code; hard-self for this seat needs **terminal_*** + KillRunning (not in-proc `cdp_shell_*`).
- Omit `board=` on turn → host auto-binds live desk seats + TM pulse (`CitizenLiveDesk`).
- After turn, host executes `@intent` routes by default on live (`CitizenRouteHost`); dry_run skips unless `execute=true` (then parses user `@intent` lines).
- Live provider turns execute `result.Routes` from assistant wire (default `execute=true`); verified GigaChat3 + mock OAI (`go=` / `drill` / `open path=` / `pane_full=` / multi-intent).
- Multiple `@intent` lines in one reply → `RouteAll` + host executes each (not first-only).
- Host execute is sync seat place + buffer open + plan REPL (`cmd=`) — not full cockpit BuildAsync (no W-spray).
- `@intent cmd=<CCL>` host-executes TM board verbs only (feature|task|done|note|…); non-plan heads refused (`refuse_non_plan_repl`).
- Cold remount: `IdeStageCycle.TryWorkspace` lazy-invokes `SetEnsure` (`WorkspaceDbHost.Ensure`) when unbound — citizen `cmd=` does not `no_workspace` without a prior cockpit warm.
- `go=plan` only places the plan organ — it does not seed/done/ship TM.
- After host execute, turn returns peer + peer_event (ADR-0028 intent_ack / intent_dropped); latch feeds next turn peer= when omitted.
- `open path=` resolves under ProjectRoot via `IdeLanguageTools.TryOpenDocument`; places `editor_scene`.
- `drill <organ>` and `detail=… scene=` place via `PlaceGo` (canonical pin, e.g. editor→editor_scene).
- `pane_full=<seat>` notes seat + places `cockpit` pointer (C-depth without W-spray).

## Entry

- `cdp_citizen` op=`scene|keys|turn`
- Keys: `CitizenAiKeys` · Completions: `CitizenCompletions` (+`.OpenAiCompat` · `.Anthropic` · `.Finish`)
- Live desk: `CitizenLiveDesk` / `IdeStageCycle.TryWorkspace`
- Route host: `CitizenRouteHost` / `CitizenIntentRouter`
- Example: `docs/design/ai-keys.example.toml`

## Antipatterns

- Starting dogfood from social/speech hubs — citizen is completions host, not CASA speech.
- Expecting live turn with empty `ai-keys.toml` (file may exist and still block).
- Treating soft-staged `.next` as remounted live seat.
- Hand-pasting `board=` for every dogfood turn when live bind exists.
- Expecting host execute from dry_run without `execute=true`.
- Committing real API keys.
- Forcing ONLY `@intent` for every dogfood when unforced multi-intent already works with live_desk.

## last_ship

- 2026-08-02 → **0.5.488**: lazy WitDB bind on `IdeStageCycle.TryWorkspace` (`SetEnsure` ← `workspace.Ensure`) — cold citizen `cmd=` no longer `no_workspace`. Dig: RunPlanCmd needed IdeStageCycle bind; bind only happened in Ensure (cockpit/etc.). Test TryWorkspace_lazy_ensure_binds_before_cmd.
- 2026-08-02 → **0.5.487**: `@intent cmd=` host-execute for TM/plan REPL (`CitizenRouteHost.RunPlanCmd` → IdeRepl → IdeTaskManager). Whitelist plan CCL heads; refuse shell/etc. Persona rule 5. Tests CitizenPlanReplHostTests (4). Dig: `go=plan` places only — gap was mutate path.
- 2026-08-02 → **0.5.485**: peer intent_ack after host execute (CitizenPeerAck) — turn surfaces peer/peer_event; latch for next inject. Dig: executed[] only, no duplex. Tests CitizenPeerAckHostTests; live FM omit board= → peer ack=2/2.
- 2026-08-02 → **0.5.484**: verify unforced multi-intent + live_desk host-execute — Execute_multi + mock OAI multi-intent channel; live FM dogfood (omit board=, no ONLY) → 2/2 `@intent go=plan` + `go=health` executed. Dig: RouteAll/live_desk already in; gap was regression+unforced proof.
- 2026-08-02 → **0.5.483**: verify `pane_full=` host-execute — Execute_pane_full + mock OAI channel; live FM dogfood. Dig: NotePaneFull already in 0.5.479; gap was regression+proof.
- 2026-08-02 → **0.5.482**: verify `drill` / `detail= scene=` host-execute — Execute_drill/detail + mock OAI drill channel; live FM dogfood. Dig: PlaceGo already in 0.5.479; gap was regression+proof.
- 2026-08-02 → **0.5.481**: verify `open path=` host-execute — Execute_open + mock OAI channel tests; live dogfood when FM emits open intent. Dig: OpenPath already in 0.5.479; gap was regression+proof.
- 2026-08-02 → **0.5.480**: verify live provider default-execute — mock OAI channel test + FM dogfood `@intent go=alert` → executed place. Dig: path already in 0.5.479; gap was regression+live proof.
- 2026-08-02 → **0.5.479**: host execute `@intent` routes after turn (`CitizenRouteHost` — place go/drill + open path). Dig gap: router returned routes only.
- 2026-08-02 → **0.5.478**: live desk auto-bind — omit `board=` → seats + TM pulse. Dig gap was empty afferent on invite-ready turns.
- 2026-08-02 → **0.5.457**: FileLines peel — `CitizenCompletions.Anthropic` + `.Finish` (main ~214L; OpenAiCompat prior).
- 2026-08-01 → **0.5.442 live**: persona HARD WIRE OUTPUT CONTRACT + OpenAI-compat `temperature=0`. Forced ONLY `@intent go=plan` → exact wire line, `wire_intents`+routes ok on GigaChat3-10B.
- Prior soft persona failed wire (prose / empty intents) — fixed by hard contract + temp=0.
- 0.5.360: Cloud.ru FM OpenAI-compat path + AiKeys helpers
- 0.5.361: `InviteReady` as serializable record
- 0.5.362: meta docs OAI/Cloud.ru on `cdp_citizen`
- 2026-08-01 earlier: dogfood had `http_402` Not enough money — billing later cleared for smoke
