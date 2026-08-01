#nullable enable

namespace CdpMcp;

/// <summary>Builtin ECL checklist catalog (Memory / AUTO / DO / ALLOW).</summary>
internal static partial class IdeChkChannel
{
    public static IReadOnlyList<ChecklistDef> Builtins() =>
    [
        new(
            "intake",
            "Before explore",
            ["phase:explore", "phase:clarify", "phase:recall"],
            [
                new("what-why", "memory", "Name what+why (or ask) before thrash", Action: "procedure:intake-brief-plan", Required: false),
                new("find-desk", "memory", "Search via desk (cdp_search / buffer find / index) — not shell/Cursor Grep", Action: "cdp_search", Required: false),
                new("files-desk", "memory", "Browse via desk (cdp_files / go=files_desk, where=project|external) — not shell ls", Action: "cdp_files", Required: false),
                new("ignite-desk", "memory", "Overnight Composer inject via CDT (cdp_ignite / go=ignite_desk) — not UIA", Action: "cdp_ignite", Required: false)
            ],
            [
                new("project", "auto", "cdp_open / project rooted", Probe: "project.open", Action: "cdp_open"),
                new("route", "do", "Route/handoff if deep topic", Action: "memory_session_route_context", Required: false)
            ]),
        new(
            "mutate",
            "Before edit",
            ["phase:act"],
            [
                new("buffer", "memory", "Mutate via cdp_buffer — not Cursor Write", Action: "cdp_buffer", Required: false),
                new("find-desk", "memory", "Search via desk (cdp_search / buffer find / index) — not shell rg", Action: "cdp_search", Required: false),
                new("files-desk", "memory", "Browse via desk (cdp_files / go=files_desk) — not shell ls/dir", Action: "cdp_files", Required: false),
                new("sa-before-refactor", "memory", "Before structural extract/split: cdp_sa / go=sa_desk (not EICAS go=sa)", Action: "cdp_sa", Required: false),
                new("debug-sa-before-act", "memory", "Before continue/rebuild under DAP: cdp_debug_sa / go=debug_desk (not go=debug raw)", Action: "cdp_debug_sa", Required: false),
                new("dap-rebuild", "memory", "debug_stop before rebuild if DAP holds PDB", Action: "cdp_debug", Probe: "dap.not_stopped", Required: false)
            ],
            [
                new("project", "auto", "Project open", Probe: "project.open", Action: "cdp_open"),
                new("sniper", "do", "Aim sniper on large files", Probe: "sniper.ok", Action: "go=scope", Required: false)
            ]),
        new(
            "plateau",
            "Plateau / no active task",
            ["phase:act+state:task.none"],
            [
                new("no-invented-stage", "memory", "Do not invent empty TM stages just to satisfy AutoIgnition", Action: "plan", Required: false),
                new("ignite-authorized", "memory", "Re-arm ignite only after operator steer or an authorized TM task exists", Action: "cdp_ignite", Required: false),
                new("pressure-stash", "memory", "Plateau invariants belong in pressure stash, not only in host summary", Action: "cdp_pressure", Required: false)
            ],
            [
                // Intentional plateau (Agent Dark Cockpit): no focus after ship is OK — not a required DO.
                new("next-task", "do", "Optional: pick/focus next TM task when continuing flight", Action: "plan", Required: false),
                new("ignite-park", "auto", "If no task exists, disarm or leave ignite parked instead of looping", Probe: "ignite.idle", Action: "cdp_ignite")
            ]),
        new(
            "verify",
            "After verify",
            ["phase:verify"],
            [
                new("tests-desk", "memory", "Tests via desk (cdp_test_sa / go=test_desk → cdp_test*) — not shell dotnet test", Action: "cdp_test_sa", Required: false)
            ],
            [
                new("problems", "auto", "Problems: no errors", Probe: "problems.clean", Action: "go=problems"),
                new("tests", "auto", "Tests not failing (or not run yet)", Probe: "tests.not_failed", Action: "cdp_test"),
                new("evidence", "do", "Claims have evidence/VC", Action: "go=report", Required: false),
                new("to-review", "do", "Then phase=review (judgment)", Action: "cdp_context", Required: false)
            ]),
        new(
            "review",
            "After review",
            ["phase:review"],
            [
                new("intent-match", "memory", "Diff matches what was asked", Required: false),
                new("blast", "memory", "Blast radius / callers considered", Required: false),
                new("scm-desk", "memory", "SCM via desk (git_scene/git_plan) — not shell status/diff/log", Action: "git_scene", Required: false),
                new("tests-desk", "memory", "Tests via desk (cdp_test_sa / go=test_desk → cdp_test*) — not shell", Action: "cdp_test_sa", Required: false)
            ],
            [
                new("board", "do", "Open review board (file cards)", Action: "go=review"),
                new("problems", "auto", "Problems still clean", Probe: "problems.clean", Action: "go=problems"),
                new("tests", "auto", "Tests not failing", Probe: "tests.not_failed", Action: "cdp_test"),
                new("slices", "do", "Logical commit slices named", Action: "git_plan", Required: false)
            ]),
        new(
            "ship",
            "Ship (commit/push)",
            ["phase:handoff", "intent:ship"],
            [
                new("secrets", "memory", "No secrets/.env in commit slices", Action: "git_preflight", Required: false),
                new("build-sa-before-ship", "memory", "Before ship: cdp_build_sa / go=build_desk (DAP lock + dirty fuse)", Action: "cdp_build_sa", Required: false),
                new("crm-gate", "memory", "Gates via CRM codes (go=crm / approved|go_around|…) — not reject essays in chat", Action: "cdp_crm", Required: false),
                new("scm-desk", "memory", "SCM via desk (git_scene/git_plan) — not shell archaeology", Action: "git_scene", Required: false)
            ],
            [
                new("git-known", "auto", "Git scene available", Probe: "git.known", Action: "git_scene"),
                new("commits", "do", "Logical commits (git_plan)", Probe: "git.clean", Action: "git_plan"),
                new("push", "allow", "Standing allow — push after ship commits (ecl unack ship push to revoke)", Action: "git_push", Required: false)
            ]),
        new(
            "dap-hold",
            "DAP stopped",
            ["state:dap.stopped"],
            [
                new("stop-before-rebuild", "memory", "debug_stop before rebuild (PDB lock)", Action: "cdp_debug", Required: false)
            ],
            [
                new("stop-context", "do", "stop_context before guessing", Action: "cdp_debug", Required: false)
            ])
    ];
}
