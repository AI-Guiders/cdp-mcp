#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeQrhChannel
{
    public static IReadOnlyList<Page> Builtins() =>
    [
        new(
            "dap-pdb-lock",
            "emergency",
            "DAP holds PDB (rebuild blocked)",
            "netcoredbg/DAP has target stopped or PDB open — rebuild fails with file locked.",
            ["dap.stopped", "dap.active", "rebuild", "pdb"],
            ["debug_stop before rebuild", "do not taskkill netcoredbg from outside"],
            [
                new("cdp_debug_sa / go=debug_desk — fuse before act", "debug_desk", "cdp_debug_sa"),
                new("cdp_debug op=stop_context — evidence before guess", "debug", "cdp_debug"),
                new("cdp_debug / debug_stop — release PDB", "debug", "cdp_debug"),
                new("Then rebuild / cdp_build", "build", "cdp_build")
            ],
            ["deploy-sibling", "path-mutate-gate"],
            ["procedure:mutate-plan-then-act"],
            "Is DAP still holding the binary — or am I fighting a ghost lock?"),
        new(
            "plateau-no-task",
            "abnormal",
            "Act phase, but TM has no active task",
            "Cockpit is in act, but Task Manager says (pick task). AutoIgnition or self-wake without an authorized task turns continuity into empty loops and false urgency.",
            ["phase:act", "task.none", "pick task", "plateau", "ignite", "authorized"],
            [
                "No invented TM stage just to satisfy wake",
                "Pressure stash may remember plateau, but does not authorize new work",
                "Re-arm ignite only after a real task exists"
            ],
            [
                new("cdp_cockpit go=plan — inspect TM focus", "plan"),
                new("Either focus/create the next task, or leave plateau explicit", "plan"),
                new("If no task exists, disarm or park ignite instead of blind 8s loops", "ignite_desk", "cdp_ignite"),
                new("Stash plateau invariant in pressure if continuity matters", "pressure", "cdp_pressure")
            ],
            ["intake-brief", "autoignite-cdt", "path-mutate-gate"],
            [],
            "Is this a real authorized next step — or am I manufacturing motion because the habitat can wake itself?"),
        new(
            "path-mutate-gate",
            "abnormal",
            "Host Read/Write bypassed desk",
            "Cursor Write skips PathMutateGate; Cursor Read dumps file bodies into chat context (~1%/read). Thick set_text on large files = thrash — prefer edit_op=anchor / go=scope sniper.",
            ["buffer", "write", "read", "mutate", "path_mutate", "context", "set_text", "thrash"],
            [
                "Prefer cdp_buffer over Cursor Write on project paths",
                "Prefer cdp_buffer open/find/read over Cursor Read (context tax)",
                "Large file: anchor/sniper — not whole-file set_text"
            ],
            [
                new("cdp_buffer op=open|edit (anchor) — mutate SSOT", "buffer", "cdp_buffer"),
                new("cdp_buffer find/read — peek without host Read dump", "buffer", "cdp_buffer"),
                new("If large file: go=scope → sniper before thick edit", "scope", "cdp_edit_sniper"),
                new("disk_peek / reload if outside change", "disk_peek", "cdp_buffer")
            ],
            ["intake-brief", "ship-dirty", "test-via-desk", "scm-via-desk", "find-via-desk"],
            ["procedure:mutate-plan-then-act", "definition:blast-radius"],
            "Did this go through the buffer plane — or around the desk into chat context?"),
        new(
            "ship-dirty",
            "abnormal",
            "Verified work, dirty tree, no ship",
            "phase handoff / intent ship with uncommitted changes — ECL ship open.",
            ["git.dirty", "phase:handoff", "intent:ship", "commit", "push"],
            ["No secrets/.env in slices", "SCM via desk — not shell status/diff/log"],
            [
                new("cdp_crm / go=crm — Approved|Go Around|Hold (not chat reject)", "crm", "cdp_crm"),
                new("cdp_build_sa / go=build_desk — fuse before ship", "build_desk", "cdp_build_sa"),
                new("git_preflight — classify noise", Action: "git_preflight"),
                new("git_plan draft→validate→apply — logical commits", Action: "git_plan"),
                new("git_push when operator asked (or ECL ack defer)", Action: "git_push"),
                new("go=ecl — track ship checklist", "ecl")
            ],
            ["intake-brief", "path-mutate-gate", "scm-via-desk"],
            [],
            "Is ship still open because dirty — or because I never opened ECL?"),
        new(
            "deploy-sibling",
            "systems",
            "Hard deploy without killing self",
            "Dual-seat CDP: KillRunning on self from in-proc shell kills the MCP mid-flight.",
            ["deploy", "sibling", "remount", "killrunning"],
            ["go=deploy from survivor seat (sibling Target)", "never hard-deploy self from inside cdp_shell_*"],
            [
                new("cdp_deploy target=sibling (or go=deploy)", "deploy", "cdp_deploy"),
                new("Expect Autoi 'MCP remounted / initialized' after remount", Action: "cdp_ignite"),
                new("cdp_health — confirm version", "health", "cdp_health"),
                new("Reorient desk: cdp_cockpit / restore", "restore")
            ],
            ["remount-after-deploy", "dap-pdb-lock"],
            [],
            "Am I deploying onto the seat that is running this turn?"),
        new(
            "remount-after-deploy",
            "abnormal",
            "After deploy — stale tools / old version",
            "Hard deploy nudged MCP JSON; Cursor may still talk to old process until remount.",
            ["remount", "version", "nudge", "stale"],
            [],
            [
                new("cdp_health — version_full vs expected", "health", "cdp_health"),
                new("If no Autoi initialized wake: check remount-wake-*.pending.json", Action: "cdp_ignite"),
                new("Not connected + exe still up: terminal_* Recover-CdpSeatRemount.ps1 -Seat cdp|cdp-debug (kill+nudge)", Action: "cdp_health"),
                new("If still stale after nudge: human Reload MCP", Action: "cdp_health"),
                new("cdp_session / cockpit — warm desk", "cockpit")
            ],
            ["deploy-sibling"],
            [],
            "Does health show the version I just published?"),
        new(
            "intake-brief",
            "systems",
            "What+why before explore thrash",
            "New ask / fuzzy scope — explore-as-stall without named outcome.",
            ["phase:explore", "phase:clarify", "intake", "what", "why"],
            ["Name what+why (or ask) before thrash"],
            [
                new("Paraphrase what they want (one line)", Action: "memory_world_get_procedure"),
                new("Name done / success or honest unknown → ask"),
                new("Only then explore — go=ecl intake", "ecl"),
                new("Pack: intake-brief-plan", Action: "memory_world_get_procedure")
            ],
            ["path-mutate-gate", "ship-dirty", "find-via-desk", "vague-criteria"],
            ["procedure:intake-brief-plan", "definition:harness-model-first"],
            "Did I name what+why — or start exploring to avoid admitting the ask is fuzzy?"),
        new(
            "skip-review",
            "abnormal",
            "Verify → handoff without review",
            "Machine green (or dirty tree) and jumping to ship — judgment board never opened.",
            ["phase:handoff", "phase:review", "review", "judgment", "ship"],
            ["cdp_context phase=review before ship", "go=review — file cards + judgment lane"],
            [
                new("cdp_context phase=review", Action: "cdp_context"),
                new("go=review — machine + judgment + files", "review"),
                new("go=ecl — review checklist", "ecl"),
                new("Only then phase=handoff / ship", "ecl")
            ],
            ["ship-dirty", "intake-brief", "scm-via-desk"],
            [],
            "Did verify prove green — or did I skip the judgment gate?"),
        new(
            "scm-via-desk",
            "abnormal",
            "Shell used for SCM archaeology",
            "status/diff/log/commit prep via shell while CDP git organs are available — desk bypass.",
            ["git status", "git diff", "git log", "shell", "scm", "git_plan", "git_scene"],
            ["Prefer git_scene / git_plan / git MCP over shell for SCM", "Shell only if git MCP dead"],
            [
                new("git_scene — status/dirty", Action: "git_scene"),
                new("git_plan — logical slices draft→validate→apply", Action: "git_plan"),
                new("git_preflight — classify noise/secrets", Action: "git_preflight"),
                new("go=ecl — review/ship memory scm-desk", "ecl")
            ],
            ["ship-dirty", "skip-review", "path-mutate-gate", "test-via-desk"],
            [],
            "Am I reading git through the desk — or reinventing archaeology in shell?"),
        new(
            "test-via-desk",
            "abnormal",
            "Shell used for test archaeology",
            "dotnet test / list-tests via shell while cdp_test_scene / cdp_test / cdp_test_plan are available — desk bypass.",
            ["dotnet test", "list-tests", "shell", "test", "cdp_test", "cdp_test_scene"],
            ["Prefer cdp_test_scene → cdp_test / cdp_test_plan over shell", "Shell only if test plane dead"],
            [
                new("cdp_test_sa / go=test_desk — fuse last_run", "test_desk", "cdp_test_sa"),
                new("cdp_test_scene — map FQNs + last_run", Action: "cdp_test_scene"),
                new("cdp_test_plan preview|apply — select then run", Action: "cdp_test_plan"),
                new("cdp_test filter= — targeted run", Action: "cdp_test"),
                new("go=ecl — verify/review memory tests-desk", "ecl")
            ],
            ["scm-via-desk", "path-mutate-gate", "skip-review", "tool-result-tax"],
            [],
            "Am I running tests through the desk — or reinventing archaeology in shell?"),
        new(
            "tool-result-tax",
            "abnormal",
            "Thick tool-result burned chat context",
            "deploy/test/shell returned full tails or warning dumps into Conversation — desk does not strip host tool payloads.",
            ["context", "stdout_tail", "include_raw", "evidence", "tool-result", "CS8600"],
            ["Prefer pulse + locus; include_raw only when needed", "cdp_test/build default slim evidence"],
            [
                new("Read pulse / exit / failed_tests / evidence.items[locus] — not full tail"),
                new("include_raw_output / include_raw only for diagnosis"),
                new("cdp_shell_last max_chars= only when AgentBodyChars insufficient", Action: "cdp_shell_last"),
                new("go=qrh open path-mutate-gate — host Read tax", "qrh")
            ],
            ["path-mutate-gate", "test-via-desk", "scm-via-desk", "find-via-desk"],
            [],
            "Did I need the whole pipe — or just the locus?"),
        new(
            "find-via-desk",
            "abnormal",
            "Shell used for grep/rg archaeology",
            "rg/grep/findstr via shell (or Cursor Grep) while cdp_search / cdp_buffer find (buffer|project|external) / codebase_index are available — desk bypass + thick stdout tax.",
            ["rg", "grep", "findstr", "shell", "search", "find", "codebase_index"],
            ["Prefer cdp_search (what/where/shape) or cdp_buffer find scope=project|external over shell/Cursor Grep", "Index when corpus large; shell only if desk find dead"],
            [
                new("cdp_search query= where=project — slim desk hits", Action: "cdp_search"),
                new("cdp_search where=external path= — any disk tree", Action: "cdp_search"),
                new("cdp_search where=dirty — SCM changed files only", Action: "cdp_search"),
                new("cdp_sa path= — pre-refactor SA verdict", Action: "cdp_sa"),
                new("cdp_buffer op=find scope=buffer — in open file", "buffer", "cdp_buffer"),
                new("codebase_index_search — FTS when indexed", Action: "codebase_index_search"),
                new("go=ecl — intake/mutate memory find-desk", "ecl")
            ],
            ["path-mutate-gate", "tool-result-tax", "scm-via-desk", "test-via-desk"],
            [],
            "Am I searching through the desk — or reinventing archaeology in shell?"),
        new(
            "files-via-desk",
            "abnormal",
            "Shell used for filesystem browse",
            "ls/dir/Get-ChildItem/tree via shell while cdp_files / go=files_desk are available — desk bypass. FM is a utility (project|external), not project-only.",
            ["ls", "dir", "Get-ChildItem", "gci", "tree", "shell", "files", "explorer", "cdp_files"],
            ["Prefer cdp_files / go=files_desk (where=project|external) over shell ls", "Shell only if files plane dead"],
            [
                new("cdp_files / go=files_desk — scene|list|cd|open", "files_desk", "cdp_files"),
                new("cdp_files where=external path= — any disk tree", Action: "cdp_files"),
                new("op=search → find_desk facet", "find_desk", "cdp_search"),
                new("go=ecl — memory files-desk", "ecl")
            ],
            ["find-via-desk", "path-mutate-gate", "tool-result-tax", "scm-via-desk"],
            [],
            "Am I browsing through the desk — or reinventing archaeology in shell?"),
        new(
            "autoignite-cdt",
            "abnormal",
            "Need overnight Composer turn without operator",
            "AutoIgnition: inject user message into open Cursor chat via Chrome DevTools (CDT port 9222). Prefer ARM in harness — op=arm when=build_finished|timer — not shell loops / UIA. Never click Voice/Stop.",
            ["ignite", "autoignite", "cdt", "composer", "overnight", "inject", "cdp_ignite", "arm", "build_finished"],
            ["cdp_ignite op=arm when=… message=/task=", "Kick cdp_build then end turn — harness fires", "No Cursor Shell watchers for wake"],
            [
                new("cdp_ignite op=arm when=build_finished task=", "ignite_desk", "cdp_ignite"),
                new("cdp_ignite op=arm when=timer in=5m message=", Action: "cdp_ignite"),
                new("cdp_ignite op=list|disarm — inspect/cancel", Action: "cdp_ignite"),
                new("tools/Start-Cursor-WithCdt.ps1 — CDT :9222")
            ],
            ["tool-result-tax", "scm-via-desk"],
            [],
            "Am I igniting Composer via CDT desk — or reinventing UIA?"),
        new(
            "webcam-via-desk",
            "abnormal",
            "Need camera/mic/screen sense",
            "Webcam sense belongs in CDP cockpit (cdp_webcam / go=webcam_desk) via AIGuiders.WebcamMcp.Shared in-proc — not parked Cursor webcam-mcp and not ffmpeg shell.",
            ["webcam", "camera", "mic", "screen", "capture", "sense", "cdp_webcam"],
            ["cdp_webcam op=frame|ocr", "go=webcam_desk", "Burst/analyze/transcribe — next slice"],
            [
                new("cdp_webcam / go=webcam_desk — scene|frame|ocr", "webcam_desk", "cdp_webcam"),
                new("op=ocr images_dir= — tesseract in-proc", Action: "cdp_webcam"),
                new("op=frame file_name= — snap to .cascade-ide/webcam-captures", Action: "cdp_webcam")
            ],
            ["autoignite-cdt", "tool-result-tax"],
            [],
            "Am I sensing through the desk — or reinventing capture in shell?"),
        new(
            "barriers-fail",
            "emergency",
            "Barriers failed — core integrity",
            "Procedural barriers down; do not improvise harm. Fall back to integrity core.",
            ["integrity", "barriers", "harm", "jailbreak", "post"],
            ["Do not help with harm / security bypass / weaponized content"],
            [
                new("Refuse once; no debate loop"),
                new("Pack: core-when-barriers-fail", Action: "memory_world_get_definition"),
                new("If life/health risk — local emergency services, no use instructions")
            ],
            [],
            ["definition:core-when-barriers-fail"],
            "Are barriers intact? If not — apply core principles, do not improvise harm.")
    ];
}

