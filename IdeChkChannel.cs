#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ecl</c> (alias <c>chk</c>) — Electronic Checklist / ECL (Boeing-style),
/// Memory Items + AUTO/DO/CONFIRM; catalog customize (add/remove/link).
/// Overlay: <c>ecl.overlay</c> (fallback <c>chk.overlay</c>); acks: <c>ecl.acks</c>.
/// </summary>
internal static partial class IdeChkChannel
{
    public const string SchemaVersion = "ecl_organ/v1";
    public const string OverlayKey = "ecl.overlay";
    public const string AcksKey = "ecl.acks";
    const string LegacyOverlayKey = "chk.overlay";
    const string LegacyAcksKey = "chk.acks";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public sealed record ProbeCtx(
        bool ProjectOpen,
        bool TaskOpen,
        bool IgniteIdle,
        bool GitKnown,
        bool GitDirty,
        bool TestsGreen,
        bool TestsFailed,
        bool ProblemsClean,
        bool DapStopped,
        bool DapActive,
        bool SniperOk,
        string Phase,
        string? Intent);

    public sealed record ItemDef(
        string Id,
        string Kind,
        string Text,
        string? Probe = null,
        string? Action = null,
        bool Required = true);

    public sealed record ChecklistDef(
        string Id,
        string Title,
        IReadOnlyList<string> Links,
        IReadOnlyList<ItemDef> MemoryItems,
        IReadOnlyList<ItemDef> Items,
        bool Builtin = true,
        bool Enabled = true);

    public sealed record ItemSnap(
        string Id,
        string Kind,
        string Text,
        bool Done,
        bool Required,
        string? Probe,
        string? Action,
        bool Acked);

    public sealed record RunSnap(
        string Id,
        string Title,
        IReadOnlyList<string> Links,
        bool Builtin,
        bool Enabled,
        bool Active,
        int Done,
        int Total,
        int OpenRequired,
        IReadOnlyList<ItemSnap> MemoryItems,
        IReadOnlyList<ItemSnap> Items);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int ActiveCount,
        int OpenRequired,
        string? HotId,
        IReadOnlyList<RunSnap> Active,
        IReadOnlyList<ChecklistDef> Catalog);

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

    public static Snap Build(ProbeCtx ctx, bool catalogOnly = false)
    {
        var catalog = EffectiveCatalog();
        var acks = LoadAcks();
        var runs = new List<RunSnap>();
        foreach (var def in catalog.Where(c => c.Enabled))
        {
            var active = catalogOnly || MatchesAny(def.Links, ctx);
            if (!catalogOnly && !active)
                continue;
            runs.Add(Evaluate(def, ctx, acks, active));
        }

        if (catalogOnly)
        {
            var enabled = catalog.Count(c => c.Enabled);
            return new Snap(
                true,
                $"ecl · catalog {enabled}/{catalog.Count}",
                0,
                0,
                null,
                runs,
                catalog);
        }

        var activeRuns = runs.Where(r => r.Active).ToList();
        var openReq = activeRuns.Sum(r => r.OpenRequired);
        var hot = activeRuns
            .OrderByDescending(r => r.OpenRequired)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var pulse = activeRuns.Count == 0
            ? "ecl · idle"
            : openReq == 0
                ? $"ecl · {activeRuns.Count} clear"
                : hot is null
                    ? $"ecl · open×{openReq}"
                    : $"ecl · {hot.Id} {hot.Done}/{hot.Total} (open×{openReq})";

        return new Snap(true, pulse, activeRuns.Count, openReq, hot?.Id, activeRuns, catalog);
    }

    public static object Handle(
        ProbeCtx ctx,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "run").Trim().ToLowerInvariant();

        object? action = null;
        switch (op)
        {
            case "list" or "catalog":
                return Board(Build(ctx, catalogOnly: true), action, "catalog");
            case "add":
                action = DoAdd(merged);
                break;
            case "remove" or "rm" or "delete":
                action = DoRemove(merged);
                break;
            case "link":
                action = DoLink(merged, add: true);
                break;
            case "unlink":
                action = DoLink(merged, add: false);
                break;
            case "enable" or "on":
                action = DoEnable(merged, enable: true);
                break;
            case "disable" or "off":
                action = DoEnable(merged, enable: false);
                break;
            case "ack" or "done" or "check":
                action = DoAck(merged);
                break;
            case "unack" or "undo":
                action = DoAck(merged, unack: true);
                break;
            case "reset":
                action = DoReset(merged);
                break;
            case "run" or "active" or "scene":
                break;
            default:
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    go = "ecl",
                    error = "unknown_op",
                    hint = "op=run|list|add|remove|link|unlink|enable|disable|ack|reset"
                };
        }

        var snap = Build(ctx);
        return Board(snap, action, "run");
    }

    public static ProbeCtx CtxFrom(
        SessionContext session,
        bool taskOpen,
        bool igniteIdle,
        bool gitKnown,
        bool gitDirty,
        bool testsGreen,
        bool testsFailed,
        bool problemsClean,
        bool dapStopped,
        bool dapActive,
        bool sniperOk)
    {
        var intent = session.Intent is { } i ? CdpEnumParse.ToWire(i) : null;
        return new ProbeCtx(
            !string.IsNullOrWhiteSpace(session.ProjectRoot),
            taskOpen,
            igniteIdle,
            gitKnown,
            gitDirty,
            testsGreen,
            testsFailed,
            problemsClean,
            dapStopped,
            dapActive,
            sniperOk,
            CdpEnumParse.ToWire(session.Phase),
            intent);
    }

    static object Board(Snap snap, object? action, string mode) => new
    {
        ok = snap.Ok,
        go = "ecl",
        schema = SchemaVersion,
        mode,
        pulse = snap.Pulse,
        title = "ECL",
        note = "Electronic Checklist (ECL) — Memory first, AUTO probes, DO/CONFIRM via ack, ALLOW = standing. Alias: go=chk.",
        active_count = snap.ActiveCount,
        open_required = snap.OpenRequired,
        hot = snap.HotId,
        runs = snap.Active.Select(RunCard).ToArray(),
        catalog = snap.Catalog.Select(c => new
        {
            id = c.Id,
            title = c.Title,
            links = c.Links,
            builtin = c.Builtin,
            enabled = c.Enabled,
            memory = c.MemoryItems.Count,
            items = c.Items.Count
        }).ToArray(),
        action,
        hint = "CCL: ecl | ecl list | ecl link ship phase:verify | ecl ack ship push | ecl add id=… link=phase:act (alias chk)"
    };

    static object RunCard(RunSnap r) => new
    {
        id = r.Id,
        title = r.Title,
        links = r.Links,
        builtin = r.Builtin,
        enabled = r.Enabled,
        active = r.Active,
        done = r.Done,
        total = r.Total,
        open_required = r.OpenRequired,
        memory_items = r.MemoryItems.Select(ItemCard).ToArray(),
        items = r.Items.Select(ItemCard).ToArray()
    };

    static object ItemCard(ItemSnap i) => new
    {
        id = i.Id,
        kind = i.Kind,
        text = i.Text,
        done = i.Done,
        required = i.Required,
        probe = i.Probe,
        action = i.Action,
        acked = i.Acked
    };
}
