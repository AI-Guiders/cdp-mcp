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
- Glass Intercom → citizen dialog: request latch `%LocalAppData%/cdp-mcp/citizen-dialog-request-LATEST.json` (shared root, not seat). Habitat `CitizenGlassDialogBridge` polls → `CitizenCompletions.Turn(mode=dialog)` → **host-execute `Routes` + `CitizenPeerAck`** (parity with `IdeCitizenChannel`, 0.5.561) → Intercom PF→PM `kind=citizen` **with Peer tip appended after prose (0.5.565)** + request latch `peer=` for Glass StatusText. Glass `/citizen` journals only (does **not** Publish human→PF voice).
- **Autoi wake consume (0.5.551→0.5.554):** when Composer unavailable + invite ready, `TryDeliverHabitatWhenComposerUnavailableAsync` calls `TryDeliverAutoiWake` → Intercom `kind=citizen` · skip CDT (`prefer_citizen`). While Cursor Composer is present, Guest Autoi CDT→Composer even if invite ready (0.5.554 — do not silent-steal).
- Omit `board=` on turn → host auto-binds live desk seats + TM pulse (`CitizenLiveDesk`).
- After turn, host executes `@intent` routes by default on live (`CitizenRouteHost`); dry_run skips unless `execute=true` (then parses user `@intent` lines).
- Live provider turns execute `result.Routes` from assistant wire (default `execute=true`); verified GigaChat3 + mock OAI (`go=` / `drill` / `open path=` / `pane_full=` / multi-intent).
- Multiple `@intent` lines in one reply → `RouteAll` + host executes each (not first-only).
- Host execute is sync seat place + buffer open/replace/**create**/**append**/**delete**/**edit(anchor)**/**undo|redo|edit_history**/**copy|cut|paste|clipboard**/**replace_all**/**back|forward|nav|recent_files**/**put** + plan REPL (`cmd=`) + **`build`** + **`test`** + **`mcp`** + **`kb`** + **`shell`** + **`debug`** + **`git`** + **`find`** + **`ide`** + **`ignite`** + **`pressure`** + **`deploy`** (wait lifecycle/outlet/`ShellHabitat.Run`/`DebugPlane`/git soft organ/`IdeFindChannel`/`IdeLanguageTools.DispatchBareAsync`/`IdeIgniteChannel.Handle`/`IdePressureChannel.Handle`/`DocumentEditPlane` edit_op=anchor|comfort undo|clip|replace_all|nav|put/`IdeDeploy.Run`, bounded) — not cockpit W-spray.
- `@intent build` / `build path=` runs session build via host-execute. `@intent test` / `test path=` / optional `filter=` runs session tests. `@intent mcp` / `mcp scene|call|mount|tools|…` dispatches outlet (`server=`/`tool=`/`preset=`). **`@intent kb`** → in-proc agent-notes pack (`memory_world`/`memory_skill`) — not guest MCP `preset=memory`. **`@intent git`** → in-proc git soft organ (default `scene`; `commit message=` / `push` / `pull` / `fetch` / `diff_scene` / `preflight`) — not shell porcelain; `go=git` only places. **`@intent find|search`** → `IdeFindChannel` (`query=` / positional / `where=` / `shape=` / `last`/`clear`); `go=find`/`go=find_desk` only place. **`@intent goto|usages|diagnostics|ide …`** → bare IDE nav (`go_to_definition`/`find_usages`/`get_diagnostics` via `DispatchBareAsync`); needs `path=` + `line=` for goto/usages. **`@intent ignite|autoi`** → `IdeIgniteChannel.Handle` (continuity|list|arm|disarm|resume; arm needs `when=`; refuse send/fire/halt/…). `go=ignite*` only places. **`@intent pressure`** → `IdePressureChannel.Handle` (scene|arm|stash|memo|line|recall|reconcile|align|ready|steer|…; stash/memo need `body=`). `go=pressure*` only places. **`@intent edit|anchor path=… anchor|at=… text|body=…`** → buffer `edit_op=anchor` (optional `place=before|after|replace`); refuse `edit_op=set_text`. **`@intent undo|redo|edit_history`** → `DocumentEditPlane` comfort (`op=undo|redo|history`, optional `path=`). **`@intent copy|cut|paste|clipboard`** → EditorComfort clip (`text=`/`anchor=`/`frame=`/`place=`). **`@intent replace_all`** → EditorComfort bulk (`query=`/`old=` + `text=`/`new=`; before PathMutate `replace`). **`@intent back|forward|nav|recent_files`** → EditorComfort nav stack/MRU. **`@intent put`** → EditorComfort draft dump (`path=`/`anchor=`/`sniper=` + `text=`/`frame=`; `overwrite=`). **`@intent scratch`** → untitled under `.cdp/scratch`. **`@intent take`** → TakeShip verify-then-ship. **`@intent share`** → IdeShare (`with=operator|self` / `from=self`). **`@intent reload|keep_disk|disk_peek`** → DocumentEditPlane disk hygiene. **`@intent scope|peek|target|aim|scope_clear`** → EditSniper.Dispatch. **`@intent deploy|hard_deploy|soft_deploy`** → `IdeDeploy.Run` (`mode=hard|soft|rollout`, optional `target=`/`dry_run=`/`force=`). `go=deploy*` only places. `@intent shell …` / `shell command="…"` runs IDE shell (optional `tab=`/`cwd=`). `@intent debug` / `debug scene|bp_list|bp_add path=… line=…|launch|…` runs DebugPlane. `@intent create|write path=… body="…"` creates via PathMutateGate (optional `overwrite=true`). `@intent append path=… body="…"` suffixes via PathMutateGate. `@intent delete|rm|remove path=…` deletes via PathMutateGate (optional `force=true` for dirty buffer; land close). `go=build` / `go=test` / `go=mcp` / `go=shell` / `go=debug` / `go=git` / `go=find` / `go=ignite` / `go=pressure` / `go=deploy` only place the organ.
- `@intent cmd=<CCL>` host-executes TM board verbs only (feature|task|done|note|…); non-plan heads refused (`refuse_non_plan_repl`).
- Cold remount: `IdeStageCycle.TryWorkspace` lazy-invokes `SetEnsure` (`WorkspaceDbHost.Ensure`) when unbound — citizen `cmd=` does not `no_workspace` without a prior cockpit warm.
- Failed plan REPL (`cmd=`) surfaces TM `error` on executed[].reason (not opaque `tm_failed`) — e.g. note on closed wall → `note needs open clock — cmd=start first`.
- `go=plan` only places the plan organ — it does not seed/done/ship TM.
- After host execute, turn returns peer + peer_event (ADR-0028 intent_ack / intent_dropped); latch feeds next turn peer= **and** `@event peer` (with `pulse | …` when Applied has pulse) into afferent inject — observe→verify without Cursor tool results.
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

- **0.5.579** — `@intent scope|peek|target|aim|scope_clear|sniper` host-execute → EditSniper.Dispatch (from=/till=/wire=/pad=). Tests CitizenSniperHostTests 7/7. Lived: primary dry_run execute → `ack=3/3` · pulses `scope armed hold=armed L10-10` / `peek …` / `clear`. Peer aim without Cursor cdp_edit_sniper.
- **0.5.578** — `@intent reload|keep_disk|disk_peek` host-execute → DocumentEditPlane disk hygiene (optional path=/pad=; pad as number). Tests CitizenDiskHostTests 6/6. Lived: cdp-debug dry_run execute → `ack=3/3` · pulses `disk_peek n=0` / `reload n=0` / `keep_disk n=0`. Peer drift recovery without Cursor buffer.
- **0.5.577** — `@intent share` host-execute → IdeShare via DocumentEditPlane (`with=operator|self`, `from=self`, path/body/ask). Tests CitizenShareHostTests 5/5. Lived: cdp-debug dry_run execute → `ack=1/1` · pulse `share operator … shared chars=845`. Peer operator delivery without loading body into agent.
- **0.5.576** — `@intent take` host-execute → TakeShip via DocumentEditPlane (path/anchor/sniper; check=/force=/vision=). Tests CitizenTakeHostTests 4/4. Lived: cdp-debug dry_run execute → `ack=1/1` · pulse `take chars=825 lines=18 skipped`. Peer verify-then-ship without Cursor buffer.
- **0.5.575** — `@intent scratch` host-execute → EditorComfort Scratch (untitled under `.cdp/scratch`; `ext=`/`text=`). Tests CitizenScratchHostTests 4/4. Lived: cdp-debug dry_run execute → `ack=1/1` · pulse `scratch untitled-1.md`. Peer blank pad without Cursor Write.
- **0.5.574** — `@intent put` host-execute → EditorComfort Put (path dump / anchor / sniper / frame=). Tests CitizenPutHostTests 6/6. Lived: cdp-debug dry_run execute → `ack=1/1` · pulse `put create chars=14`. Peer draft dump without Cursor Write.
- **0.5.573** — `@intent back|forward|nav|recent_files` host-execute → EditorComfort via DocumentEditPlane. Tests CitizenNavHostTests 6/6. Lived: cdp-debug dry_run execute → `ack=3/3` · back locus + nav pulse + recent_files n=6. Peer Navigate Backward/Forward without Cursor MCP.
- **0.5.572** — `@intent replace_all` host-execute → EditorComfort via DocumentEditPlane (`query=`/`old=` + `text=`/`new=`; routed before PathMutate `replace`). Tests CitizenReplaceAllHostTests 5/5. Lived: primary dry_run execute → `ack=1/1` · pulse `replace_all n=2`. Peer bulk rename without Cursor MCP.
- **0.5.571** — `@intent copy|cut|paste|clipboard|clip_clear` host-execute → EditorComfort via DocumentEditPlane. Tests CitizenClipHostTests 5/5. Lived: cdp-debug dry_run execute → `ack=2/2` · copy c1 + clipboard frames=1. Peer clip hand without Cursor MCP.
- **0.5.570** — `@intent undo|redo|edit_history` host-execute → `DocumentEditPlane` EditorComfort (`op=undo|redo|history`, optional `path=`). Tests CitizenUndoHostTests 5/5. Lived: primary dry_run execute → `ack=1/1` · pulse `undo replace undo=0 redo=1` + redo ack. Peer buffer recovery without Cursor MCP.
- **0.5.569** — `@intent deploy|hard_deploy|soft_deploy` host-execute → `IdeDeploy.Run` (mode=hard|soft|rollout; target=/dry_run=/force=). `go=deploy*` place-only. Tests CitizenDeployHostTests 5/5. Lived: cdp-debug dry_run execute → `ack=1/1` · deploy hard dry_run. Peer remount path without Cursor MCP.
- **0.5.568** — `@intent edit|anchor` host-execute → `DocumentEditPlane` `edit_op=anchor` (path+anchor/at+text/body; place=before|after|replace). Refuse `edit_op=set_text`. Tests CitizenEditHostTests 5/5. Lived: primary dry_run execute → `ack=1/1` · pulse `edit anchor place=before`. Peer precise mutate without Cursor Write / string-replace-only.
- **0.5.567** — `@intent pressure` host-execute → `IdePressureChannel.Handle` (scene/arm/stash/memo/line/recall/gate). `go=pressure*` place-only. Tests CitizenPressureHostTests 5/5. Lived: dry_run execute on cdp-debug → `ack=1/1` · stash ARMED · recall·ready. Peer L1 without Cursor MCP.
- **0.5.566** — `@intent ignite|autoi` host-execute → `IdeIgniteChannel.Handle` (continuity|list|arm|disarm|resume). `go=ignite*` place-only. Tests CitizenIgniteHostTests 6/6. Lived: dry_run execute on primary → `ack=1/1` · arm last_once timer · due stamped. Overnight peer re-ARM without Cursor MCP.
- **0.5.565** — Glass→Citizen PeerAck surface: after hands, Intercom body appends peer tip + request latch `peer=`; Glass `CitizenDialogRequestStatus` paints `done · peer`. Tests bridge 4/4 · Glass status 3/3. Lived latch `c27ace9928e9` → Cloud.ru `go=plan` · Intercom body ends `ack=1/1` · dual seats 0.5.565.
- **0.5.564** — `@intent delete|rm|remove path=…` host-execute → PathMutateGate `DocumentBufferStore.Delete` + land close (remaining Write-surface hand). Dirty buffer needs `force=true`. Tests CitizenDeleteHostTests 5/5. Lived: dry_run execute on cdp-debug → ack delete · scratch gone.
- **2026-08-03 lived dogfood (0.5.561 chain)** — unforced Glass latch → Cloud.ru Qwen dialog → FM `@intent git` (no stuffed wire) → host Execute seats `M:git_scene` same-second stamp · Intercom `kind=citizen` · latch `28e189ea7ab9` pending→done. Hands parity not just forced `go=health`.
- **0.5.561** — Glass dialog bridge host-execute parity: `CitizenGlassDialogBridge` runs `CitizenRouteHost.Execute` + `CitizenPeerAck.FromExecuted` after dialog Turn (talk≠hands gap closed). Tests CitizenGlassDialogBridgeTests 4/4.
- **0.5.558** — `@intent goto|usages|diagnostics|ide` → `IdeLanguageTools.DispatchBareAsync` (peer nav without Cursor Roslyn). Calendar full-dedication densest. Tests 6/6.
- **0.5.557** — `@intent find|search` e2e → `IdeFindChannel` (peer dig without Cursor Grep). Calendar steer: sick_leave densest. Tests 7/7. VL #63. Lived: 9 hits dry_run execute.
- **0.5.556** — `@intent git` e2e host-execute: scene/diff/preflight + commit/push/pull/fetch (not observe-only). `go=git` place-only. Live dogfood: scene dirty → self-commit `4b01419` → push → ok-parse fix `17a220b`. VL #62. Dig: PathMutate host_write arch residual; operator rejected thin peel.
- **0.5.555** — `@intent kb` → in-proc `memory_world`/`memory_skill` (pack/KB), not guest MCP `preset=memory`. VL #58. Dig: persona taught wrong memory surface.
- **0.5.554** — Autoi Composer-first while Cursor host: Guest CDT→Composer; `prefer_citizen` only when Composer unavailable. VL #57.
- **0.5.514** — citizen `@intent append path=… body="…"` host-execute → PathMutateGate open+suffix+Flush + land open (end-of-file without Cursor Write / needle). Not write-alias. Dig: idle-PF skip Composer parked (Autoi overnight). VL #20.
- **0.5.513** — citizen `@intent create|write path=… body="…"` host-execute → PathMutateGate `Create` + land open (greenfield without Cursor Write). Alias write/text=; overwrite=true. VL #19. Dig: replace covered edit; create was remaining Write gap.
- **0.5.512 + full-chain dogfood** — live FM observe→act→verify (health place → shell echo) with organ width; VL #18. Debug organ host-execute same version.
- **0.5.512** — citizen `@intent debug` host-execute → `DebugPlane.DispatchAsync` (scene/bp_*/launch/…; path=+line= for bp_add). Dig: go=debug place-only. VL #17.
- **0.5.507** — citizen `@intent build` / `build path=` host-execute → `IdeSessionLifecycle.BuildAsync` (sync wait, 3m bound) + place `build` organ. Dig: `go=build` placed only; PF organ parity needs real compile. Persona wire examples + Session/Build binders in Program.
- **0.5.506** — citizen `open`/`replace` publish `land-LATEST` (`NavigationLandLatch`) so Glass LatchHub opens the path (disk peel alone skips when file not open). Projector feels partner invent.
- **0.5.505** — citizen `@intent replace path=… old="…" new="…"` host-execute via buffer PathMutateGate (`TryReplaceInDocument`). Complements open; gated disk mutate so partner need not Cursor Write.
 — citizen `@frame` gains Intercom **presence** line (`presence | @PF … · @PM …`) from `CideIntercomPresenceLatch.AfferentLine` (Glass latch SSOT). Complements `peer=` (MCP health). Persona + inject test.

- 2026-08-02: **Citizen full chain CLOSED** — live request latch dogfood (`citizen-dialog-request-LATEST` pending→done + Intercom `kind=citizen`); Glass StatusText watches request status (`CitizenDialogRequestStatus` + LatchHub); habitat dialog turn + bridge already **0.5.496**.
- 2026-08-02 → **0.5.496**: Glass Intercom `/citizen` → request latch + habitat bridge → citizen dialog reply as Intercom `kind=citizen` (no guest PF unread). Tests CitizenGlassDialogBridgeTests 3/3.
- 2026-08-02 → **0.5.495**: dialog memory deepen — afferent `dialog|` + sticky facts (`CitizenStickyFacts`, op=sticky); window 40 msgs; persona Memory clause. Baseline dogfood: МАЯК + агентка + Света recall.
- 2026-08-02 → **0.5.494**: persona equal standing + Света/Who identity split (dialog+wire); Intercom operator default Света.
- 2026-08-02 → **0.5.493**: dialog multi-turn memory (`CitizenDialogHistory` seat jsonl) — mode=dialog prepends prior pairs; op=history|clear; reset=/history= on turn.
- 2026-08-02 → **full-chain live dogfood (0.5.512 organs)**: soft wire ask → Qwen `@intent go=health` → host place; next soft observe → `@intent shell echo full-chain-ok` → `shell ok exit=0` + peer pulse. Unforced live provider + observe-act-verify with organ width (no dry_run paste).
- 2026-08-02 → **0.5.512**: `@intent debug` host-execute waits `DebugPlane.DispatchAsync` (scene/bp_list/bp_add/…). Dig: go=debug place-only.
- 2026-08-02 → **0.5.511**: `@intent shell` host-execute waits `ShellHabitat.Run` (command= or rest; optional tab=/cwd=). Dig: go=shell place-only; cmd= still refuses non-plan. Debug organ still separate peel.
- 2026-08-02 → **0.5.510**: `@intent mcp` host-execute waits `McpOutletHabitat.DispatchAsync` (scene/call/mount/tools/unmount/presets). Dig: go=mcp place-only; outlet Instance + test override.
- 2026-08-02 → **0.5.509**: `@intent test` host-execute waits `IdeSessionLifecycle.TestAsync` (path= + optional filter=). Dig: go=test place-only; build pattern 0.5.507. Live dogfood filter→`test ok 1/1`. Quoted `path=` (spaces) via ExtractKeyedValue. MCP facade still separate peel. VL #14.
- 2026-08-02 → **0.5.508**: multi-turn observe peel — peer_event latch injects into next afferent (wire+dialog) with `pulse | …`; peer= tip carries first pulse. Persona: treat ack pulse as observe evidence. Tests pulse ack + Build_wire_injects_latched_peer_event.
- 2026-08-02 → **0.5.507**: `@intent build` host-execute waits `IdeSessionLifecycle.BuildAsync` (bounded). Dig: go=build place-only. VL #12.
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
