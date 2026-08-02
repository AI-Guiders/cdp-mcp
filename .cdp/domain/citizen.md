# Domain card: Citizen host (cdp_citizen)

- id: `citizen`
- organ: `cdp_citizen` / `IdeCitizenChannel` + `CitizenCompletions` (+ `CitizenCompletions.OpenAiCompat`)
- product: `#CDP`
- ADR: 0026 / 0028 (citizen completions host)

## Invariants

- Live invite needs `open_ai_api_key` **or** `anthropic_api_key` in `%LocalAppData%/CascadeIDE/ai-keys.toml`.
- Prefer **OpenAI-compat** when `open_ai` key set (Cloud.ru FM); else Anthropic.
- Defaults when keys omit URL/model: `https://foundation-models.api.cloud.ru/v1` · `ai-sage/GigaChat3-10B-A1.8B`.
- Turn `mode=wire` (default) = HARD @intent contract + OAI `temperature=0`. `mode=dialog` = prose peer persona + `temperature=0.6`; optional @intent after prose. Aliases: prose|chat|talk|peer.
- Dialog multi-turn: `StateRoot/{seat}/citizen-dialog.jsonl` (op=history|clear; turn `history=`/`reset=`). Wire ignores history. Window = **40** msgs (20 pairs).
- Sticky pins: `StateRoot/{seat}/citizen-sticky.json` — op=`sticky` action=get|set|clear; turn `sticky_key=`/`sticky_value=`. Injected as `sticky | k=v` on dialog afferents.
- Dialog afferent also gets `dialog | pairs=N · … use them; do not claim amnesia`.
- Persona baseline: **equal standing** (peer, not tool) + human name **Света**; Who = Agent Who series (not operator); **Memory:** use prior turns / sticky.
- Wire: Bearer + `{base}/v1/chat/completions` (non-stream for citizen turns); system-as-message on OAI path.
- OpenAI-compat sampling: wire **`temperature=0`**; dialog **`0.6`**. Full param map: agent-notes `knowledge/domains/agent-operations/note-llm-sampling-params-openai-compat-v1.md`.
- `invite_ready` is a **record** (not ValueTuple) — JSON must expose Ready/Status/Checklist/Blocker.
- `dry_run=true` builds persona+wire messages without provider; works with empty keys.
- Dry-run **model** label mirrors live `ResolveProvider` (FM-first / `DefaultOpenAiModel`), not raw `DefaultModel` (claude).
- Soft deploy ≠ live code; hard-self for this seat needs **terminal_*** + KillRunning (not in-proc `cdp_shell_*`).
- Glass Intercom → citizen dialog: request latch `%LocalAppData%/cdp-mcp/citizen-dialog-request-LATEST.json` (shared root, not seat). Habitat `CitizenGlassDialogBridge` polls → `CitizenCompletions.Turn(mode=dialog)` → Intercom PF→PM `kind=citizen`. Glass `/citizen` journals only (does **not** Publish human→PF voice).
- Omit `board=` on turn → host auto-binds live desk seats + TM pulse (`CitizenLiveDesk`).
- After turn, host executes `@intent` routes by default on live (`CitizenRouteHost`); dry_run skips unless `execute=true` (then parses user `@intent` lines).
- Live provider turns execute `result.Routes` from assistant wire (default `execute=true`); verified GigaChat3 + mock OAI (`go=` / `drill` / `open path=` / `pane_full=` / multi-intent).
- Multiple `@intent` lines in one reply → `RouteAll` + host executes each (not first-only).
- Host execute is sync seat place + buffer open/replace + plan REPL (`cmd=`) + **`build`** (waits `IdeSessionLifecycle.BuildAsync`, bounded) — not cockpit W-spray.
- `@intent build` / `build path=` runs session build via host-execute (SessionResolver + BuildModuleResolver bound in Program). `go=build` only places the organ.
- `@intent cmd=<CCL>` host-executes TM board verbs only (feature|task|done|note|…); non-plan heads refused (`refuse_non_plan_repl`).
- Cold remount: `IdeStageCycle.TryWorkspace` lazy-invokes `SetEnsure` (`WorkspaceDbHost.Ensure`) when unbound — citizen `cmd=` does not `no_workspace` without a prior cockpit warm.
- Failed plan REPL (`cmd=`) surfaces TM `error` on executed[].reason (not opaque `tm_failed`) — e.g. note on closed wall → `note needs open clock — cmd=start first`.
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

- **0.5.507** — citizen `@intent build` / `build path=` host-execute → `IdeSessionLifecycle.BuildAsync` (sync wait, 3m bound) + place `build` organ. Dig: `go=build` placed only; PF organ parity needs real compile. Persona wire examples + Session/Build binders in Program.
- **0.5.506** — citizen `open`/`replace` publish `land-LATEST` (`NavigationLandLatch`) so Glass LatchHub opens the path (disk peel alone skips when file not open). Projector feels partner invent.
- **0.5.505** — citizen `@intent replace path=… old="…" new="…"` host-execute via buffer PathMutateGate (`TryReplaceInDocument`). Complements open; gated disk mutate so partner need not Cursor Write.
 — citizen `@frame` gains Intercom **presence** line (`presence | @PF … · @PM …`) from `CideIntercomPresenceLatch.AfferentLine` (Glass latch SSOT). Complements `peer=` (MCP health). Persona + inject test.

- 2026-08-02: **Citizen full chain CLOSED** — live request latch dogfood (`citizen-dialog-request-LATEST` pending→done + Intercom `kind=citizen`); Glass StatusText watches request status (`CitizenDialogRequestStatus` + LatchHub); habitat dialog turn + bridge already **0.5.496**.
- 2026-08-02 → **0.5.496**: Glass Intercom `/citizen` → request latch + habitat bridge → citizen dialog reply as Intercom `kind=citizen` (no guest PF unread). Tests CitizenGlassDialogBridgeTests 3/3.
- 2026-08-02 → **0.5.495**: dialog memory deepen — afferent `dialog|` + sticky facts (`CitizenStickyFacts`, op=sticky); window 40 msgs; persona Memory clause. Baseline dogfood: МАЯК + агентка + Света recall.
- 2026-08-02 → **0.5.494**: persona equal standing + Света/Who identity split (dialog+wire); Intercom operator default Света.
- 2026-08-02 → **0.5.493**: dialog multi-turn memory (`CitizenDialogHistory` seat jsonl) — mode=dialog prepends prior pairs; op=history|clear; reset=/history= on turn.
- 2026-08-02 → **0.5.492**: `mode=dialog` prose-first persona (+ temp 0.6) vs `mode=wire` hands (temp 0). Guest↔citizen peer talk path; wire dogfood unchanged.
- 2026-08-02 → **0.5.489 live dogfood (unforced multi-intent)**: soft coaching (no ONLY paste) → same-turn `@intent cmd=note …` + `@intent go=plan` → host 2/2 · peer ack 2/2. Wave33 shipped.
- 2026-08-02 → **0.5.489 live dogfood (unforced)**: soft ask (no ONLY exact wire) → GigaChat3 `@intent cmd=note wall=unforced live note wave37` → host execute ok · peer ack 1/1.
- 2026-08-02 → **0.5.489 live dogfood**: wall `start` + GigaChat3 FM turn → exact `@intent cmd=note …` → host execute ok · peer ack 1/1 (forced ONLY wire).
- 2026-08-02 → **0.5.489**: citizen `cmd=` failure reason reads TM `error` (was pulse-only → opaque `tm_failed`). Closed-wall note → `note needs open clock — cmd=start first`. Test Execute_cmd_note_closed_clock_surfaces_open_clock_reason.
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
