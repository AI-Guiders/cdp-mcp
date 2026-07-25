using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE cockpit — MFD + loci + desk dispatcher + <b>scan-pattern seats</b> (ADR 0191)
/// + tile manager (ADR 0189). Modes: nav | sys | chk. <c>cmd=</c> REPL; <c>go=</c> places organ in seat.
/// </summary>
internal static class IdeCockpit
{
    public const string SchemaVersion = "cockpit/v1.15";
    public const int GoResultCapChars = 24_000;
    public const int GoPulseCapChars = 1_200;
    public const int MaxTiles = 4;

    /// <summary>Exposed for Tools → Options desk.default_layout choices.</summary>
    public static string[] LayoutPresetIds =>
        LayoutPresets.Keys
            .Concat(IdeDeskSeats.PresetIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsKnownGoVerb(string verb) => GoMap.ContainsKey(verb);
    public static bool IsKnownPinAlias(string alias) => PinAliases.ContainsKey(alias);

    /// <summary>Canonical seat organ pin (aliases → plan/editor_scene/…).</summary>
    public static string CanonicalOrganPin(string organPin)
    {
        var pin = organPin.Trim().ToLowerInvariant();
        return PinAliases.TryGetValue(pin, out var canon) ? canon : pin;
    }

    static readonly object PinGate = new();
    static List<string> StickyPins = [];

    static readonly Dictionary<string, string[]> LayoutPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code+net"] = ["editor_scene", "browser"],
        ["code+shell"] = ["editor_scene", "shell"],
        ["code+git"] = ["editor_scene", "git_scene"],
        ["net+shell"] = ["browser", "shell"],
        ["desk"] = ["editor_scene", "browser", "shell"],
        ["cockpit"] = ["editor_scene", "browser", "shell"],
        ["code+net+shell"] = ["editor_scene", "browser", "shell"],
        ["agent"] = ["plan", "editor_scene", "script_scene"],
    };

    static readonly Dictionary<string, string> PinAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["editor"] = "editor_scene",
        ["editor_scene"] = "editor_scene",
        ["code"] = "editor_scene",
        ["buffer"] = "buffer_scene",
        ["buffer_scene"] = "buffer_scene",
        ["browser"] = "browser",
        ["net"] = "browser",
        ["internet"] = "browser",
        ["internet_browser"] = "browser",
        ["scene_internet_browser"] = "browser",
        ["shell"] = "shell_scene",
        ["shell_scene"] = "shell_scene",
        ["git"] = "git_scene",
        ["git_scene"] = "git_scene",
        ["debug"] = "debug_scene",
        ["debug_scene"] = "debug_scene",
        ["test"] = "test_scene",
        ["test_scene"] = "test_scene",
        ["mcp"] = "mcp_scene",
        ["mcp_scene"] = "mcp_scene",
        ["settings"] = "settings",
        ["settings_scene"] = "settings",
        ["ide_settings"] = "settings",
        ["prefs"] = "settings",
        ["options"] = "settings",
        ["correspondence"] = "correspondence",
        ["corr"] = "correspondence",
        ["work"] = "plan",
        ["tasks"] = "plan",
        ["plan"] = "plan",
        ["task"] = "plan",
        ["feature"] = "plan",
        ["tm"] = "plan",
        ["report"] = "report",
        ["evidence"] = "report",
        ["pfd"] = "report",
        ["alert"] = "alert",
        ["eicas"] = "alert",
        ["project"] = "project_scene",
        ["project_scene"] = "project_scene",
    };

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    static readonly HashSet<string> MfdPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "sys", "chk", "gates"
    };

    /// <summary>Allowlist desk verbs → organ tools. Cockpit stays a пульт, not the organ.</summary>
    static readonly Dictionary<string, (string Tool, Dictionary<string, JsonElement>? Defaults)> GoMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["editor_scene"] = ("cdp_editor_scene", null),
            ["editor"] = ("cdp_editor_scene", null),
            ["edit_draft"] = ("cdp_edit_plan", Dict(("op", "draft"))),
            ["edit_plan"] = ("cdp_edit_plan", Dict(("op", "draft"))),
            ["scope"] = (EditSniper.ToolName, Dict(("op", "scope"))),
            ["target"] = (EditSniper.ToolName, Dict(("op", "target"))),
            ["peek"] = (EditSniper.ToolName, Dict(("op", "peek"))),
            ["scope_clear"] = (EditSniper.ToolName, Dict(("op", "clear"))),
            ["sniper"] = (EditSniper.ToolName, Dict(("op", "status"))),
            ["buffer_scene"] = ("cdp_buffer", Dict(("op", "scene"))),
            ["buffer"] = ("cdp_buffer", Dict(("op", "scene"))),
            ["reload"] = ("cdp_buffer", Dict(("op", "reload"))),
            ["keep_disk"] = ("cdp_buffer", Dict(("op", "keep_disk"))),
            ["disk_peek"] = ("cdp_buffer", Dict(("op", "disk_peek"))),
            ["undo"] = ("cdp_buffer", Dict(("op", "undo"))),
            ["redo"] = ("cdp_buffer", Dict(("op", "redo"))),
            ["history"] = ("cdp_buffer", Dict(("op", "history"))),
            ["copy"] = ("cdp_buffer", Dict(("op", "copy"))),
            ["cut"] = ("cdp_buffer", Dict(("op", "cut"))),
            ["paste"] = ("cdp_buffer", Dict(("op", "paste"))),
            ["put"] = ("cdp_buffer", Dict(("op", "put"))),
            ["dump"] = ("cdp_buffer", Dict(("op", "put"))),
            ["take"] = ("take", null),
            ["get_take"] = ("get_take", null),
            ["ship"] = ("take", null),
            ["paste_sniper"] = ("cdp_buffer", Dict(("op", "paste"), ("sniper", "true"), ("place", "replace"))),
            ["put_sniper"] = ("cdp_buffer", Dict(("op", "put"), ("sniper", "true"), ("place", "replace"))),
            ["clipboard"] = ("cdp_buffer", Dict(("op", "clipboard"))),
            ["clip"] = ("cdp_buffer", Dict(("op", "clipboard"))),
            ["clip_clear"] = ("cdp_buffer", Dict(("op", "clipboard_clear"))),
            ["clipboard_clear"] = ("cdp_buffer", Dict(("op", "clipboard_clear"))),
            ["find"] = ("find", null),
            ["get_find"] = ("get_find", null),
            ["find_all"] = ("find_all", null),
            ["find_in_files"] = ("find_in_files", null),
            ["fif"] = ("find_in_files", null),
            ["replace_all"] = ("cdp_buffer", Dict(("op", "replace_all"))),
            ["back"] = ("cdp_buffer", Dict(("op", "back"))),
            ["forward"] = ("cdp_buffer", Dict(("op", "forward"))),
            ["recent_files"] = ("cdp_buffer", Dict(("op", "recent_files"))),
            ["scratch"] = ("cdp_buffer", Dict(("op", "scratch"))),
            ["git_scene"] = ("git_git_scene", null),
            ["git"] = ("git_git_scene", null),
            ["git_draft"] = ("git_git_plan", Dict(("op", "draft"))),
            ["git_plan"] = ("git_git_plan", Dict(("op", "draft"))),
            ["test_scene"] = ("cdp_test_scene", null),
            ["test"] = ("cdp_test_scene", null),
            ["test_plan"] = ("cdp_test_plan", Dict(("op", "preview"))),
            ["analysis_scene"] = ("cdp_analysis_scene", null),
            ["analysis"] = ("cdp_analysis_scene", null),
            ["clones"] = ("cdp_analysis_scene", Dict(("feature", "clones"))),
            ["correspondence"] = ("cdp_analysis_scene", Dict(("feature", "correspondence"))),
            ["corr"] = ("cdp_analysis_scene", Dict(("feature", "correspondence"))),
            ["semantic_map"] = ("cdp_analysis_scene", Dict(("feature", "semantic_map"))),
            ["semantic"] = ("cdp_analysis_scene", Dict(("feature", "semantic_map"))),
            ["related"] = ("cdp_analysis_scene", Dict(("feature", "semantic_map"))),
            ["complete"] = ("get_completions", null),
            ["completions"] = ("get_completions", null),
            ["intellisense"] = ("get_completions", null),
            ["signature_help"] = ("get_signature_help", null),
            ["sighelp"] = ("get_signature_help", null),
            ["script_scene"] = ("cdp_script_scene", null),
            ["script"] = ("cdp_script_scene", null),
            ["script_put"] = ("cdp_script_scene", Dict(("op", "put"))),
            ["script_open"] = ("cdp_script_scene", Dict(("op", "open"))),
            ["script_check"] = ("cdp_script_scene", Dict(("op", "check"))),
            ["script_run"] = ("cdp_script_scene", Dict(("op", "run"))),
            ["script_last"] = ("cdp_script_scene", Dict(("op", "last"))),
            ["script_help"] = ("cdp_script_scene", Dict(("op", "help"))),
            ["goto"] = ("cdp_goto", null),
            ["go_to"] = ("cdp_goto", null),
            ["t"] = ("cdp_goto", null),
            // Goto Feature (code nav) — not Task Manager; TM uses soft organ + plan aliases.
            ["q"] = ("cdp_goto", Dict(("kind", "feature"))),
            ["goto_feature"] = ("cdp_goto", Dict(("kind", "feature"))),
            ["shell_scene"] = ("cdp_shell_scene", null),
            ["shell"] = ("cdp_shell_scene", null),
            ["shell_last"] = ("cdp_shell_last", null),
            ["debug_scene"] = ("cdp_debug", Dict(("op", "scene"))),
            ["debug"] = ("cdp_debug", Dict(("op", "scene"))),
            ["build"] = ("cdp_build", null),
            ["project_scene"] = ("cdp_project_scene", null),
            ["project"] = ("cdp_project_scene", null),
            // Plan = Task Manager organ (Feature/Task vocabulary). work/tasks/tm = aliases.
            ["plan"] = ("cdp_work", Dict(("op", "tasks"))),
            ["work"] = ("cdp_work", Dict(("op", "tasks"))),
            ["tasks"] = ("cdp_work", Dict(("op", "tasks"))),
            ["task"] = ("cdp_work", Dict(("op", "tasks"))),
            ["feature"] = ("cdp_work", Dict(("op", "tasks"))),
            ["tm"] = ("cdp_work", Dict(("op", "tasks"))),
            ["restore"] = ("cdp_restore", null),
            ["restore_previous"] = ("cdp_restore", null),
            ["previous"] = ("cdp_restore", null),
            ["desk_restore"] = ("cdp_restore", null),
            ["navigate"] = ("cdp_land", null),
            ["land"] = ("cdp_land", null),
            ["deep_link"] = ("cdp_land", null),
            ["deeplink"] = ("cdp_land", null),
            ["mcp_scene"] = ("cdp_mcp", Dict(("op", "scene"))),
            ["mcp"] = ("cdp_mcp", Dict(("op", "scene"))),
            ["mcp_presets"] = ("cdp_mcp", Dict(("op", "presets"))),
            ["mcp_mount"] = ("cdp_mcp", Dict(("op", "mount"))),
            ["mcp_tools"] = ("cdp_mcp", Dict(("op", "tools"))),
            ["mcp_call"] = ("cdp_mcp", Dict(("op", "call"))),
            ["mcp_unmount"] = ("cdp_mcp", Dict(("op", "unmount"))),
            ["scene_internet_browser"] = ("cdp_browser", Dict(("op", "scene"))),
            ["internet_browser"] = ("cdp_browser", Dict(("op", "scene"))),
            ["internet_browser_scene"] = ("cdp_browser", Dict(("op", "scene"))),
            ["browser_scene"] = ("cdp_browser", Dict(("op", "scene"))),
            ["browser"] = ("cdp_browser", Dict(("op", "scene"))),
            ["internet_browser_open"] = ("cdp_browser", Dict(("op", "open"))),
            ["internet_browser_dump"] = ("cdp_browser", Dict(("op", "dump"))),
            ["internet_browser_links"] = ("cdp_browser", Dict(("op", "links"))),
            ["internet_browser_follow"] = ("cdp_browser", Dict(("op", "follow"))),
            ["internet_browser_which"] = ("cdp_browser", Dict(("op", "which"))),
            ["internet_browser_search"] = ("cdp_browser", Dict(("op", "search"))),
            ["search"] = ("cdp_browser", Dict(("op", "search"))),
            ["settings"] = ("cdp_settings", Dict(("op", "options"))),
            ["settings_scene"] = ("cdp_settings", Dict(("op", "options"))),
            ["ide_settings"] = ("cdp_settings", Dict(("op", "options"))),
            ["prefs"] = ("cdp_settings", Dict(("op", "options"))),
            ["options"] = ("cdp_settings", Dict(("op", "options"))),
            ["tools_options"] = ("cdp_settings", Dict(("op", "options"))),
            ["options_page"] = ("cdp_settings", Dict(("op", "page"))),
            ["settings_page"] = ("cdp_settings", Dict(("op", "page"))),
            ["settings_catalog"] = ("cdp_settings", Dict(("op", "catalog"))),
            ["settings_get"] = ("cdp_settings", Dict(("op", "get"))),
            ["settings_set"] = ("cdp_settings", Dict(("op", "set"))),
            ["settings_unset"] = ("cdp_settings", Dict(("op", "unset"))),
            ["settings_which"] = ("cdp_settings", Dict(("op", "which"))),
            ["lsp_probe"] = ("cdp_settings", Dict(("op", "lsp_probe"))),
            ["lsp_status"] = ("cdp_settings", Dict(("op", "lsp_probe"))),
            ["lsp_install"] = ("cdp_settings", Dict(("op", "lsp_install"))),
            ["lsp_ensure"] = ("cdp_settings", Dict(("op", "lsp_ensure"))),
            ["lsp_add"] = ("cdp_settings", Dict(("op", "lsp_add"))),
            ["languages"] = ("cdp_settings", Dict(("op", "page"), ("page", "languages"))),
            ["languages_page"] = ("cdp_settings", Dict(("op", "page"), ("page", "languages"))),
        };

    static Dictionary<string, JsonElement> Dict(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }

    /// <summary>VS Ctrl+Q — fuzzy desk verbs / organs (not code).</summary>
    public static FeatureHit[] SearchFeatures(string query, int max)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return [];

        static int Score(string name, string query)
        {
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 1000;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 800;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return 500;
            return 0;
        }

        return GoMap.Keys
            .Select(go => (go, score: Score(go, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.go, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => new FeatureHit(x.go, x.score, GoMap[x.go].Tool))
            .ToArray();
    }

    public readonly record struct FeatureHit(string Go, int Score, string Tool);

    readonly record struct SeatPane(
        string Seat,
        string? Organ,
        bool Empty,
        bool Full,
        bool Ok,
        string Line,
        object? Pane)
    {
        public object ToSlot() => new
        {
            seat = Seat,
            glyph = IdeDeskView.SeatGlyph(Seat),
            organ = Organ,
            label = IdeDeskView.ShortOrgan(Organ),
            empty = Empty,
            ok = Ok,
            line = Line,
            full = Full
        };

        public object ToCard(bool includePane) => includePane
            ? new
            {
                seat = Seat,
                organ = Organ,
                empty = Empty,
                ok = Ok,
                line = Line,
                full = Full,
                pane = Pane
            }
            : new
            {
                seat = Seat,
                organ = Organ,
                empty = Empty,
                ok = Ok,
                line = Line,
                full = Full
            };
    }

    sealed class Locus(
        string Id,
        string Kind,
        string Pulse,
        string Drill,
        string? Go = null,
        object? Detail = null)
    {
        public string Id { get; } = Id;
        public string Kind { get; } = Kind;
        public string Pulse { get; } = Pulse;
        public string Drill { get; } = Drill;
        public string? Go { get; } = Go;
        public object? Detail { get; } = Detail;

        public object Card() => new
        {
            id = Id,
            kind = Kind,
            pulse = Pulse,
            drill = Drill,
            go = Go
        };
    }

    public static async Task<string> BuildAsync(
        SessionContext session,
        DocumentBufferStore docStore,
        ShellHabitat shellHabitat,
        InternetBrowserHabitat internetBrowser,
        IdeSettingsHabitat ideSettings,
        McpOutletHabitat mcpOutlet,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        object? replDirect = null;
        var cmdLine = OptString(args, "cmd") ?? OptString(args, "line") ?? OptString(args, "repl")
            ?? OptString(args, "ccl") ?? OptString(args, "ccc");
        if (cmdLine is { Length: > 0 })
        {
            var applied = IdeRepl.Apply(cmdLine, args);
            if (applied is { } a)
            {
                args = a.Args;
                replDirect = a.Direct;
            }
        }

        var mfd = OptString(args, "mfd") ?? OptString(args, "page") ?? IdeSettingsHabitat.EffectiveDeskMfd();
        mfd = mfd.Trim().ToLowerInvariant();
        if (!MfdPages.Contains(mfd))
            mfd = "nav";

        var focusId = OptString(args, "locus") ?? OptString(args, "focus");
        var includeSubmodules = BoolOr(args, "include_submodules", false);
        var goVerb = OptString(args, "go") ?? OptString(args, "do");

        // Soft MFD switches via go=chk|sys|nav (no organ dispatch).
        if (goVerb is { Length: > 0 } && MfdPages.Contains(goVerb.Trim()))
        {
            mfd = goVerb.Trim().ToLowerInvariant();
            goVerb = null;
        }

        // Soft tile / seat verbs: go=tiles|layout|seats|repl (no organ).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("tiles", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("layout", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tile", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("seats", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("seat", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("repl", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ccl", StringComparison.OrdinalIgnoreCase)))
        {
            goVerb = null;
        }

        ApplyDeskMutation(args);
        var deskCleared = BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false)
            || BoolOr(args, "seat_clear", false) || BoolOr(args, "clear_seats", false);
        if (IdeDeskSeats.IsSeatsMode())
        {
            if (!deskCleared)
                IdeDeskSeats.EnsureDefaultsFromSettings();
            // Cheerful sit: sticky report without evidence → plan (not !report).
            CheerIdleReportSeat(session);
        }
        else
            EnsureDefaultLayoutFromSettings();

        object? goResult = replDirect;
        // Buffer before go= so locus=buffer:doc-N can inject path= into reload/keep_disk/disk_peek.
        var buffer = CollectBuffer(docStore.Scene());
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("quality", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("gates", StringComparison.OrdinalIgnoreCase)))
        {
            // Soft organ: quality gates scene (not a separate MCP tool in v0).
            mfd = "gates";
            var path = OptString(args, "path");
            if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
                && ga.TryGetProperty("path", out var gp) && gp.ValueKind == JsonValueKind.String)
                path ??= gp.GetString();
            var q = string.IsNullOrWhiteSpace(path)
                ? QualityGates.EvaluateStore(docStore, session.ProjectRoot)
                : QualityGates.EvaluatePath(docStore, session.ProjectRoot, path!);
            goResult = new
            {
                ok = true,
                go = "quality",
                tool = "quality_gates",
                detail = "full",
                truncated = false,
                result = q
            };
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("quality");
            goVerb = null;
        }

        // Soft organ: report / evidence board (ADR 0193 — last probe body).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("report", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("evidence", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("pfd", StringComparison.OrdinalIgnoreCase)))
        {
            goResult = IdeReportBoard.Handle(session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("report");
            goVerb = null;
        }

        // Defer alert soft organ until after Collect* (needs buffer/debug snaps).
        var wantAlert = goVerb is { Length: > 0 }
            && (goVerb.Equals("alert", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("eicas", StringComparison.OrdinalIgnoreCase));
        if (wantAlert)
            goVerb = null;

        // Soft organ: Plan / Task Manager (Feature → Task tree, WitDB sticky focus).
        if (goVerb is { Length: > 0 }
            && (goVerb.Equals("plan", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("work", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tasks", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("tm", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("task", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("feature", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("promote", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("confirm", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("reject", StringComparison.OrdinalIgnoreCase)))
        {
            if (workspaceStore is null)
            {
                goResult = new
                {
                    ok = false,
                    go = "plan",
                    error = "no_workspace",
                    hint = "Intent workspace WitDB unavailable."
                };
            }
            else
            {
                var tmArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
                if (session.ProjectRoot is { Length: > 0 } pr)
                    tmArgs["project_root"] = JsonSerializer.SerializeToElement(pr);
                if (!tmArgs.ContainsKey("tm_op")
                    && goVerb is "feature" or "task" or "promote" or "confirm" or "reject"
                    && (!tmArgs.TryGetValue("go_args", out var gax)
                        || gax.ValueKind != JsonValueKind.Object
                        || !gax.TryGetProperty("op", out _)))
                {
                    tmArgs["tm_op"] = JsonSerializer.SerializeToElement(
                        goVerb.Equals("feature", StringComparison.OrdinalIgnoreCase) ? "feature"
                        : goVerb.Equals("task", StringComparison.OrdinalIgnoreCase) ? "task"
                        : goVerb.Equals("promote", StringComparison.OrdinalIgnoreCase) ? "promote"
                        : goVerb.Equals("confirm", StringComparison.OrdinalIgnoreCase) ? "confirm"
                        : goVerb.Equals("reject", StringComparison.OrdinalIgnoreCase) ? "reject"
                        : "board");
                }

                goResult = IdeTaskManager.Handle(workspaceStore, workspaceState, tmArgs);
            }

            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("plan");
            goVerb = null;
        }

        // World snaps early — seat pulse + scene-only go= reuse (no double/triple organ thrash).
        var git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
        var shell = CollectShell(shellHabitat.Scene());
        var browser = internetBrowser.Pulse();
        var mcpPulse = mcpOutlet.Pulse();
        var settingsPulse = ideSettings.Pulse();

        // Soft world scene go= (pulse only): place seat, skip DispatchGoAsync.
        var goDetailEarly = (OptString(args, "go_detail") ?? "pulse").Trim().ToLowerInvariant();
        if (goVerb is { Length: > 0 }
            && IdeWorldChannel.IsWorldSceneGo(goVerb)
            && goDetailEarly is not "full"
            && !args.ContainsKey("go_args"))
        {
            var pin = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
            goResult = WorldSnapPane(pin, git, shell, browser, mcpPulse);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(pin))
                IdeDeskSeats.PlaceOrgan(pin);
            goVerb = null;
        }

        if (goVerb is { Length: > 0 })
        {
            var pin = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
            goResult = await DispatchGoAsync(goVerb.Trim(), args, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(pin))
                IdeDeskSeats.PlaceOrgan(pin);
            // Re-collect after organ may have mutated buffers (reload/keep_disk/edit…).
            buffer = CollectBuffer(docStore.Scene());
            // World organs may have mutated habitat — refresh cheap pulses.
            if (IdeWorldChannel.IsWorldOrgan(pin))
            {
                shell = CollectShell(shellHabitat.Scene());
                browser = internetBrowser.Pulse();
                mcpPulse = mcpOutlet.Pulse();
                if (CanonicalOrganPin(pin) is "git_scene")
                    git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
            }
        }

        var debug = CollectDebug(session);
        var test = CollectTest(session);
        var work = CollectWork(workspaceStore, workspaceState);
        var quality = QualityGates.Snap(docStore, session.ProjectRoot);
        var alertSnap = IdeAlertChannel.Build(
            quality, buffer.DiskChangedCount, debug.ActiveDap, debug.Stopped);

        // Soft organ: alert after snaps exist (quality + disk + DAP).
        if (wantAlert)
        {
            goResult = IdeAlertChannel.Handle(
                quality, buffer.DiskChangedCount, debug.ActiveDap, debug.Stopped, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("alert");
        }

        var loci = BuildLoci(session, git, shell, browser, settingsPulse, buffer, debug, test, work, quality);
        var next = BuildNext(session, git, shell, buffer, debug, test, work, focusId, quality, alertSnap);

        // Sniper locus appears when a corridor is held (desk pulse, not organ dump).
        if (EditSniper.HasHold)
        {
            loci.Insert(Math.Min(1, loci.Count), new Locus(
                "edit:sniper",
                "sniper",
                $"aim {EditSniper.PulseLine}",
                "go=target → go=edit_draft | go=scope_clear",
                "target",
                EditSniper.HoldCard()));
        }

        object? focus = null;
        if (!string.IsNullOrWhiteSpace(focusId))
        {
            var hit = loci.FirstOrDefault(l =>
                string.Equals(l.Id, focusId, StringComparison.OrdinalIgnoreCase));
            focus = hit is null
                ? new { ok = false, locus = focusId, reason = "unknown_locus", hint = "Pick id from loci[]." }
                : new
                {
                    ok = true,
                    locus = hit.Id,
                    kind = hit.Kind,
                    pulse = hit.Pulse,
                    drill = hit.Drill,
                    go = hit.Go,
                    detail = hit.Detail
                };
        }

        object? page = mfd switch
        {
            "sys" => BuildSysPage(session, git, shell, buffer, debug, test, work),
            "chk" => BuildChkPage(session, git, shell, buffer, debug, test),
            "gates" => QualityGates.EvaluateStore(docStore, session.ProjectRoot),
            _ => BuildNavPage(loci, focus)
        };

        var goVerbs = GoMap.Keys
            .Concat(["quality", "gates", "tiles", "layout", "tile", "seats", "seat", "repl", "ccl", "tasks", "plan", "feature", "task", "promote", "confirm", "reject", "report", "evidence", "alert", "eicas"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var seatsMode = IdeDeskSeats.IsSeatsMode();
        object? seats = null;
        object? tiles = null;
        var pins = SnapshotPins();
        var tileLayout = OptString(args, "layout");
        string?[] seatPinList = [];

        if (seatsMode)
        {
            var seatMap = IdeDeskSeats.Snapshot();
            seatPinList = IdeDeskSeats.Order.Select(s => seatMap[s]).ToArray();
            var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
            var seatsDetail = (OptString(args, "seats_detail") ?? OptString(args, "view_detail") ?? "compact")
                .Trim().ToLowerInvariant();
            var wantPanes = seatsDetail is "full" or "panes"
                || BoolOr(args, "seats_panes", false)
                || BoolOr(args, "compact", true) == false;
            var hasProject = !string.IsNullOrWhiteSpace(session.ProjectRoot);
            var seatPanes = new List<SeatPane>();
            foreach (var seatId in IdeDeskSeats.Order)
            {
                var organ = seatMap[seatId];
                if (organ is not { Length: > 0 })
                {
                    seatPanes.Add(new SeatPane(seatId, null, true, false, true, "(empty)", null));
                    continue;
                }

                var wantFull = fullPane is { Length: > 0 }
                    && (string.Equals(fullPane, organ, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fullPane, seatId, StringComparison.OrdinalIgnoreCase)
                        || (PinAliases.TryGetValue(fullPane, out var fa)
                            && string.Equals(fa, organ, StringComparison.OrdinalIgnoreCase)));

                // Quiet seat: organs that thrash without cdp_open — no dispatch / no Application Data noise.
                // Plan stays live (WitDB offline). Editor/browser quiet until project.
                if (!hasProject && !wantFull && IdeDeskView.OrganNeedsProject(organ))
                {
                    var quiet = QuietNoProjectPane(organ);
                    var (qOk, qLine) = IdeDeskView.LineFromPane(quiet, false, organ);
                    seatPanes.Add(new SeatPane(seatId, organ, false, false, qOk, qLine, quiet));
                    continue;
                }

                var tileArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var kv in args)
                    tileArgs[kv.Key] = kv.Value;
                tileArgs["go_detail"] = JsonSerializer.SerializeToElement(wantFull ? "full" : "pulse");
                // Cockpit steer must not leak into organ dispatch (else browser gets op=feature → unknown_op).
                tileArgs.Remove("go");
                tileArgs.Remove("do");
                tileArgs.Remove("cmd");
                tileArgs.Remove("line");
                tileArgs.Remove("repl");
                tileArgs.Remove("go_args");
                tileArgs.Remove("tm_op");
                tileArgs.Remove("seat");
                tileArgs.Remove("organ");
                tileArgs.Remove("pin");
                tileArgs.Remove("layout");
                tileArgs.Remove("pins");
                tileArgs.Remove("tiles");
                tileArgs.Remove("pane_full");
                tileArgs.Remove("full_pane");
                tileArgs.Remove("seats_detail");
                tileArgs.Remove("view_detail");
                tileArgs.Remove("desk_detail");
                tileArgs.Remove("nav_detail");
                tileArgs.Remove("locus");
                tileArgs.Remove("focus");
                tileArgs.Remove("mfd");
                tileArgs.Remove("page");
                tileArgs.Remove("pin_clear");
                tileArgs.Remove("clear_pins");
                tileArgs.Remove("seat_clear");
                tileArgs.Remove("clear_seats");

                object pane;
                var planPin = CanonicalOrganPin(organ);
                if (planPin is "plan")
                {
                    // Soft organ — seat must not route through GoMap/cdp_work defaults alone.
                    if (workspaceStore is null)
                    {
                        pane = new
                        {
                            ok = false,
                            go = "plan",
                            error = "no_workspace",
                            hint = "Intent workspace WitDB unavailable."
                        };
                    }
                    else
                    {
                        var board = IdeTaskManager.Handle(workspaceStore, workspaceState, tileArgs);
                        pane = wantFull
                            ? new
                            {
                                ok = true,
                                go = "plan",
                                tool = "cdp_work",
                                detail = "full",
                                truncated = false,
                                result = board
                            }
                            : board;
                    }
                }
                else if (planPin is "report" or "evidence" or "pfd")
                {
                    var board = IdeReportBoard.Handle(session, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "report",
                            tool = "report_board",
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "alert" or "eicas")
                {
                    var board = IdeAlertChannel.Handle(
                        quality, buffer.DiskChangedCount, debug.ActiveDap, debug.Stopped, tileArgs);
                    pane = wantFull
                        ? new
                        {
                            ok = true,
                            go = "alert",
                            tool = "alert_channel",
                            detail = "full",
                            truncated = false,
                            result = board
                        }
                        : board;
                }
                else if (planPin is "quality" or "gates")
                {
                    var q = QualityGates.EvaluateStore(docStore, session.ProjectRoot);
                    pane = wantFull
                        ? new { ok = true, go = "quality", tool = "quality_gates", detail = "full", truncated = false, result = q }
                        : new { ok = true, go = "quality", detail = "pulse", pulse = QualityGates.Snap(docStore, session.ProjectRoot).Pulse, result = q };
                }
                else if (!wantFull && IdeWorldChannel.IsWorldOrgan(planPin))
                {
                    // World channel: reuse cockpit snaps — never re-dispatch scene on every desk pulse.
                    pane = WorldSnapPane(planPin, git, shell, browser, mcpPulse);
                }
                else if (!wantFull && planPin is ("editor_scene" or "buffer_scene" or "editor" or "buffer"))
                {
                    pane = EditorSnapPane(buffer);
                }
                else if (!wantFull && planPin is ("script_scene" or "script" or "probe"))
                {
                    var (sok, sp) = ScriptScene.Pulse(session);
                    pane = new { ok = sok, go = "script_scene", detail = "pulse", pulse = sp, snap = true };
                }
                else
                {
                    pane = await DispatchGoAsync(organ, tileArgs, buffer, focusId, dispatch, cancellationToken)
                        .ConfigureAwait(false);
                }

                var (ok, line) = IdeDeskView.LineFromPane(pane, false, organ);
                seatPanes.Add(new SeatPane(seatId, organ, false, wantFull, ok, line, pane));
                if (wantFull)
                    wantPanes = true;
            }

            var viewSlots = seatPanes
                .Select(s => new IdeDeskView.Slot(s.Seat, s.Organ, s.Empty, s.Ok, s.Line, s.Full))
                .ToArray();
            var view = IdeDeskView.Build(viewSlots);
            var includePanes = wantPanes;
            // Root payload owns view once — seats/tiles keep slots only (cockpit/v1.8).
            seats = IdeDeskSeats.Card(
                seatPanes.Select(s => s.ToSlot()).ToList(),
                includePanes ? seatPanes.Select(s => s.ToCard(true)).ToList() : null);
            // Seats mode: no legacy tiles blob (view once at root).
            tiles = null;

            var deskDetail = ResolveDeskDetail(args, focusId);
            var wantNav = deskDetail is "nav" or "full";
            var payload = new Dictionary<string, object?>
            {
                ["schema"] = SchemaVersion,
                ["ok"] = true,
                ["role"] = "desk",
                ["mode"] = "seats",
                ["view"] = view,
                ["mfd"] = mfd,
                ["mfd_pages"] = new[] { "nav", "sys", "chk", "gates" },
                ["session"] = SessionPulse(session),
                ["desk_detail"] = deskDetail,
                ["seats"] = seats,
                ["tiles"] = tiles,
                ["pins"] = seatPinList.Where(x => x is { Length: > 0 }).ToArray(),
                ["layouts"] = LayoutPresetIds,
                ["next"] = next,
                ["focus"] = focus,
                ["page"] = page,
                ["go"] = goResult,
                ["warm"] = warm,
                ["alert"] = IdeAlertChannel.PulseCard(alertSnap),
                ["hint"] = wantNav
                    ? "Read view.banner / view.ascii first. Steer: cmd=\"go alert\" | layout=agent. " +
                      "seats_detail=full or pane_full= for organ dump."
                    : "Slim desk (cockpit/v1.15): view + seats + next + alert. " +
                      "Cold auto-restore. desk_detail=nav for loci[]; cmd=alert|probe|report|plan (CCL)."
            };
            if (wantNav)
            {
                payload["loci"] = loci.Select(l => l.Card()).ToArray();
                payload["go_verbs"] = goVerbs;
            }

            return JsonSerializer.Serialize(payload, Pretty);
        }

        {
            var requestPins = ResolveRequestedPins(args);
            var tilePins = requestPins.Count > 0 ? requestPins : pins;
            if (tilePins.Count > 0)
            {
                var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
                tiles = await BuildTilesAsync(
                        tilePins,
                        tileLayout,
                        fullPane,
                        args,
                        buffer,
                        focusId,
                        dispatch,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var deskDetailTiles = ResolveDeskDetail(args, focusId);
        var wantNavTiles = deskDetailTiles is "nav" or "full";
        var tilesPayload = new Dictionary<string, object?>
        {
            ["schema"] = SchemaVersion,
            ["ok"] = true,
            ["role"] = "desk",
            ["mode"] = "tiles",
            ["mfd"] = mfd,
            ["mfd_pages"] = new[] { "nav", "sys", "chk", "gates" },
            ["session"] = SessionPulse(session),
            ["desk_detail"] = deskDetailTiles,
            ["seats"] = null,
            ["tiles"] = tiles,
            ["pins"] = pins.ToArray(),
            ["layouts"] = LayoutPresetIds,
            ["next"] = next,
            ["focus"] = focus,
            ["page"] = page,
            ["go"] = goResult,
            ["warm"] = warm,
            ["alert"] = IdeAlertChannel.PulseCard(alertSnap),
            ["hint"] = "desk.mode=tiles (legacy). Prefer seats. desk_detail=nav for loci/go_verbs."
        };
        if (wantNavTiles)
        {
            tilesPayload["loci"] = loci.Select(l => l.Card()).ToArray();
            tilesPayload["go_verbs"] = goVerbs;
        }

        return JsonSerializer.Serialize(tilesPayload, Pretty);
    }

    static string ResolveDeskDetail(IReadOnlyDictionary<string, JsonElement> args, string? focusId)
    {
        var raw = (OptString(args, "desk_detail") ?? OptString(args, "nav_detail") ?? "slim")
            .Trim().ToLowerInvariant();
        if (raw is "compact")
            raw = "slim";
        // Focused locus needs the nav catalog.
        if (focusId is { Length: > 0 } && raw is "slim" or "omit")
            return "nav";
        if (raw is "slim" or "omit" or "nav" or "full")
            return raw is "omit" ? "slim" : raw;
        return "slim";
    }


    static object WorldSnapPane(
        string organ,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        McpOutletHabitat.McpPulse mcp)
    {
        var pin = CanonicalOrganPin(organ);
        return pin switch
        {
            "git_scene" => IdeWorldChannel.Pane("git_scene", git is not null, GitPulseLine(git)),
            "shell_scene" => IdeWorldChannel.Pane(
                "shell_scene",
                true,
                shell.Running > 0
                    ? $"shell · {shell.TabCount} tab(s) · {shell.Running} running"
                    : $"shell · {shell.TabCount} tab(s)"),
            "browser" => IdeWorldChannel.Pane("browser", browser.Ok, browser.Line),
            "mcp_scene" => IdeWorldChannel.Pane("mcp_scene", mcp.Ok, mcp.Line),
            _ => IdeWorldChannel.Pane(pin, true, pin)
        };
    }

    static object EditorSnapPane(BufferSnap buffer)
    {
        var pulse = buffer.Count == 0
            ? "—"
            : buffer.DiskChangedCount > 0
                ? $"{buffer.Count} buf · disk×{buffer.DiskChangedCount}"
                : buffer.DirtyCount > 0
                    ? $"{buffer.Count} buf · dirty×{buffer.DirtyCount}"
                    : $"{buffer.Count} buf";
        return new
        {
            ok = true,
            go = "editor_scene",
            detail = "pulse",
            pulse,
            snap = true,
            hint = "pane_full=editor for dump"
        };
    }

    static object QuietNoProjectPane(string organ) => new
    {
        ok = true,
        go = organ,
        detail = "pulse",
        pulse = "no project — cdp_open",
        quiet = true,
        hint = "cdp_open first; pane_full= to force organ dump anyway."
    };

    static async Task<object> DispatchGoAsync(
        string verb,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var detail = (OptString(cockpitArgs, "go_detail") ?? "pulse").Trim().ToLowerInvariant();
        if (detail is not ("pulse" or "full"))
            detail = "pulse";

        if (verb.Equals("cockpit", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("cdp_cockpit", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                ok = false,
                go = verb,
                error = "refuse_self",
                hint = "go= routes to organs; use mfd=/locus= for cockpit itself."
            };
        }

        if (!GoMap.TryGetValue(verb, out var map))
        {
            return new
            {
                ok = false,
                go = verb,
                error = "unknown_go",
                hint = "Pick from go_verbs[] or next[].go / locus.go."
            };
        }

        var callArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (map.Defaults is not null)
        {
            foreach (var kv in map.Defaults)
                callArgs[kv.Key] = kv.Value;
        }

        if (cockpitArgs.TryGetValue("go_args", out var goArgs) && goArgs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in goArgs.EnumerateObject())
                callArgs[p.Name] = p.Value.Clone();
        }

        InjectBufferPathFromLocus(verb, callArgs, buffer, focusId);

        try
        {
            var raw = await dispatch(map.Tool, callArgs, cancellationToken).ConfigureAwait(false);
            if (detail == "full")
            {
                var capped = CapGoResult(raw, GoResultCapChars);
                object? parsed = TryParseJson(capped.Text);
                return new
                {
                    ok = true,
                    go = verb,
                    tool = map.Tool,
                    detail = "full",
                    truncated = capped.Truncated,
                    result = parsed
                };
            }

            var pulse = PulseFromOrgan(raw);
            return new
            {
                ok = pulse.Ok,
                go = verb,
                tool = map.Tool,
                detail = "pulse",
                pulse = pulse.Line,
                schema = pulse.Schema,
                next = pulse.Next,
                hint = pulse.Hint ?? "go_detail=full for organ dump; or call organ tool directly."
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                go = verb,
                tool = map.Tool,
                detail,
                error = ex.Message
            };
        }
    }

    /// <summary>
    /// Desk comfort: <c>locus=buffer:doc-N</c> + <c>go=reload|keep_disk|disk_peek</c>
    /// scopes to that file when <c>path=</c> / <c>go_args.path</c> omitted.
    /// </summary>
    static void InjectBufferPathFromLocus(
        string verb,
        Dictionary<string, JsonElement> callArgs,
        BufferSnap buffer,
        string? focusId)
    {
        if (verb is not ("reload" or "keep_disk" or "disk_peek"))
            return;
        if (callArgs.TryGetValue("path", out var pathEl)
            && pathEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(pathEl.GetString()))
            return;
        if (focusId is not { Length: > 0 }
            || !focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            || focusId.Equals("buffer:none", StringComparison.OrdinalIgnoreCase))
            return;

        var docId = focusId["buffer:".Length..];
        var doc = buffer.Docs.FirstOrDefault(d =>
            string.Equals(d.DocId, docId, StringComparison.OrdinalIgnoreCase));
        if (doc is null || string.IsNullOrWhiteSpace(doc.Path) || doc.Path == "?")
            return;

        callArgs["path"] = JsonSerializer.SerializeToElement(doc.Path);
    }

    static void EnsureDefaultLayoutFromSettings()
    {
        lock (PinGate)
        {
            if (StickyPins.Count > 0) return;
            var layout = IdeSettingsHabitat.EffectiveDeskLayout();
            if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout, out var preset))
                StickyPins = preset.Take(MaxTiles).ToList();
        }
    }

    /// <summary>Seats (default) or legacy tile pin mutations.</summary>
    static void ApplyDeskMutation(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (IdeDeskSeats.IsSeatsMode())
        {
            if (BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false)
                || BoolOr(args, "seat_clear", false) || BoolOr(args, "clear_seats", false))
            {
                IdeDeskSeats.Clear();
                return;
            }

            if (IdeDeskSeats.TryParseSeatAssignment(args, out var seat, out var organ)
                && seat is not null && organ is not null)
            {
                var pin = ResolvePinName(organ) ?? organ;
                IdeDeskSeats.TryPlaceExplicit(seat, pin);
                return;
            }

            var layout = OptString(args, "layout");
            if (layout is { Length: > 0 } && IdeDeskSeats.TryApplyPreset(layout))
                return;

            // pins= in seats mode: interpret as scan-order fill P,F,M (replace, not append).
            var pins = ParsePinList(args, "pins") ?? ParsePinList(args, "tiles");
            if (pins is { Count: > 0 })
            {
                IdeDeskSeats.Clear();
                for (var i = 0; i < Math.Min(pins.Count, IdeDeskSeats.Order.Length); i++)
                    IdeDeskSeats.TryPlaceExplicit(IdeDeskSeats.Order[i], pins[i]);
            }

            return;
        }

        ApplyPinMutation(args);
    }

    static string? ResolvePinName(string verb)
    {
        if (PinAliases.TryGetValue(verb, out var canon))
            return canon;
        return GoMap.ContainsKey(verb) ? verb : null;
    }

    /// <summary>Sticky report with no evidence → sit on plan (cheerful cold desk).</summary>
    static void CheerIdleReportSeat(SessionContext session)
    {
        var map = IdeDeskSeats.Snapshot();
        if (!map.TryGetValue("p", out var organ) || organ is not { Length: > 0 })
            return;
        if (CanonicalOrganPin(organ) is not "report")
            return;
        if (IdeReportBoard.HasEvidence(session))
            return;
        IdeDeskSeats.PlaceOrgan("plan");
    }

    static bool IsPlaceableOrgan(string pin)
    {
        if (PinAliases.ContainsKey(pin))
            return true;
        // Scene-like go verbs that own a seat pulse (not clipboard / find one-shots).
        return pin is "editor_scene" or "buffer_scene" or "browser" or "shell_scene" or "git_scene"
            or "debug_scene" or "test_scene" or "mcp_scene" or "settings" or "project_scene"
            or "plan" or "work" or "report" or "evidence" or "pfd" or "alert" or "eicas"
            or "correspondence" or "quality" or "gates" or "analysis_scene"
            or "script_scene" or "semantic_map";
    }

    static void ApplyPinMutation(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (BoolOr(args, "pin_clear", false) || BoolOr(args, "clear_pins", false))
        {
            lock (PinGate) StickyPins = [];
            return;
        }

        var layout = OptString(args, "layout");
        if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout.Trim(), out var preset))
        {
            lock (PinGate) StickyPins = preset.Take(MaxTiles).ToList();
            return;
        }

        var pins = ParsePinList(args, "pins") ?? ParsePinList(args, "tiles");
        if (pins is { Count: > 0 })
        {
            lock (PinGate) StickyPins = pins.Take(MaxTiles).ToList();
            return;
        }

        var add = ParsePinList(args, "pin");
        if (add is { Count: > 0 })
        {
            lock (PinGate)
            {
                foreach (var p in add)
                {
                    if (!StickyPins.Contains(p, StringComparer.OrdinalIgnoreCase) && StickyPins.Count < MaxTiles)
                        StickyPins.Add(p);
                }
            }
        }
    }

    static List<string> SnapshotPins()
    {
        lock (PinGate) return StickyPins.ToList();
    }

    static List<string> ResolveRequestedPins(IReadOnlyDictionary<string, JsonElement> args)
    {
        var layout = OptString(args, "layout");
        if (layout is { Length: > 0 } && LayoutPresets.TryGetValue(layout.Trim(), out var preset))
            return preset.Take(MaxTiles).ToList();
        return ParsePinList(args, "pins") ?? ParsePinList(args, "tiles") ?? [];
    }

    static List<string>? ParsePinList(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;

        var raw = new List<string>();
        if (el.ValueKind == JsonValueKind.String)
        {
            raw.AddRange((el.GetString() ?? "")
                .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    raw.Add(s.Trim());
            }
        }
        else
            return null;

        var resolved = new List<string>();
        foreach (var r in raw)
        {
            if (PinAliases.TryGetValue(r, out var canon))
            {
                if (!resolved.Contains(canon, StringComparer.OrdinalIgnoreCase))
                    resolved.Add(canon);
            }
            else if (GoMap.ContainsKey(r) && !resolved.Contains(r, StringComparer.OrdinalIgnoreCase))
                resolved.Add(r);
        }

        return resolved.Count == 0 ? null : resolved;
    }

    static async Task<object> BuildTilesAsync(
        IReadOnlyList<string> pins,
        string? layout,
        string? fullPane,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var panes = new List<object>();
        foreach (var pin in pins.Take(MaxTiles))
        {
            var wantFull = fullPane is { Length: > 0 }
                && (string.Equals(fullPane, pin, StringComparison.OrdinalIgnoreCase)
                    || (PinAliases.TryGetValue(fullPane, out var fa)
                        && string.Equals(fa, pin, StringComparison.OrdinalIgnoreCase)));

            var tileArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in cockpitArgs)
                tileArgs[kv.Key] = kv.Value;
            tileArgs["go_detail"] = JsonSerializer.SerializeToElement(wantFull ? "full" : "pulse");
            // Don't re-apply go= from parent into every pane.
            tileArgs.Remove("go");
            tileArgs.Remove("do");

            var pane = await DispatchGoAsync(pin, tileArgs, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
            panes.Add(new
            {
                pin,
                full = wantFull,
                pane
            });
        }

        return new
        {
            ok = true,
            role = "tiles",
            layout,
            pins,
            count = panes.Count,
            panes,
            hint = "Human twin: code + browser side-by-side. Drill one pane: go=<pin> go_detail=full; or pane_full=<pin>."
        };
    }

    sealed record OrganPulse(bool Ok, string Line, string? Schema, object? Next, string? Hint);

    static OrganPulse PulseFromOrgan(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.String)
            {
                var pulseLine = pulseEl.GetString() ?? "";
                if (pulseLine.Length > 0)
                {
                    var hintEarly = root.TryGetProperty("hint", out var h0) && h0.ValueKind == JsonValueKind.String
                        ? Truncate(h0.GetString(), 240)
                        : null;
                    var schemaEarly = root.TryGetProperty("schema", out var sch0) && sch0.ValueKind == JsonValueKind.String
                        ? sch0.GetString()
                        : null;
                    return new OrganPulse(ok, Truncate(pulseLine, GoPulseCapChars) ?? pulseLine, schemaEarly, null, hintEarly);
                }
            }

            var schema = root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String
                ? sch.GetString()
                : null;
            var hint = root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                ? Truncate(h.GetString(), 240)
                : null;
            object? next = null;
            if (root.TryGetProperty("next", out var n))
                next = JsonSerializer.Deserialize<JsonElement>(n.GetRawText());

            var bits = new List<string>();
            if (schema is { Length: > 0 })
                bits.Add(schema);
            bits.Add(ok ? "ok" : "FAIL");

            void AddNum(string key, string label)
            {
                if (root.TryGetProperty(key, out var el) && el.TryGetInt32(out var n))
                    bits.Add($"{label}={n}");
            }

            AddNum("count", "n");
            AddNum("dirty_count", "dirty");
            AddNum("disk_changed_count", "disk");
            AddNum("candidate_count", "cand");
            AddNum("slice_count", "slices");
            AddNum("path_count", "paths");
            AddNum("tab_count", "tabs");
            AddNum("groups", "groups");
            AddNum("files_scanned", "files");
            AddNum("undo_left", "undo");
            AddNum("redo_left", "redo");
            AddNum("replaced", "replaced");

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                bits.Add(Truncate(err.GetString(), 80) ?? "error");

            // git_scene often nests roots
            if (root.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
                bits.Add($"roots={roots.GetArrayLength()}");

            var line = string.Join(' ', bits);
            if (line.Length > GoPulseCapChars)
                line = line[..GoPulseCapChars] + "…";
            return new OrganPulse(ok, line, schema, next, hint);
        }
        catch
        {
            var line = Truncate(raw, GoPulseCapChars) ?? "";
            return new OrganPulse(true, line, null, null, "go_detail=full for parseable dump");
        }
    }

    static object? TryParseJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch
        {
            return text;
        }
    }

    static (string Text, bool Truncated) CapGoResult(string raw, int cap)
    {
        if (raw.Length <= cap)
            return (raw, false);
        return (raw[..cap] + "\n…[cockpit go.result truncated]", true);
    }

    static object[] BuildNext(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        string? focusId,
        QualityGates.QualitySnap quality,
        IdeAlertChannel.Snap alert)
    {
        var list = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string go, string label, string why)
        {
            if (list.Count >= 8 || !seen.Add(go))
                return;
            list.Add(new { id, go, label, why });
        }

        if (session.ProjectRoot is null)
        {
            Add("n-open", "project_scene", "Project map", "No project — cdp_open / project_scene first");
            if (File.Exists(DeskBookmark.FilePath))
                Add("n-restore", "restore", "Restore Previous", "desk bookmark — project + buffers (not LLM chat)");
            if (work.IntentId is not null)
                Add("n-plan", "plan", "Task Manager", work.Pulse ?? work.IntentId);
            else
                Add("n-plan", "plan", "Task Manager", "no plan — feature <name>");
            Add("n-settings", "options", "Tools → Options", "IDE prefs — internet/desk/shell/mcp (not Cursor)");
            return list.ToArray();
        }

        // EICAS-lite: surface alert before comfort next when something beeps.
        if (alert.Level != IdeAlertChannel.Level.Clear)
            Add("n-alert", "alert", "Alert board", alert.Pulse);

        Add("n-goto", "goto", "Go To (Ctrl+T)", "query= type/member/file — land on anchor");
        Add("n-editor", "editor_scene", "Editor map", "Buffer/desk loop");

        // Dual-instance / post hard-deploy: Restore Previous desk bookmark.
        if (File.Exists(DeskBookmark.FilePath))
            Add("n-restore", "restore", "Restore Previous", "desk bookmark — project + buffers (not LLM chat)");

        if (EditorComfort.AnyUndo())
            Add("n-undo", "undo", "Undo last edit", "buffer edit stack");
        if (EditorComfort.AnyClipboard())
            Add("n-clipboard", "clipboard", "Clipboard", "frames — pick frame= + paste");
        if (EditorComfort.AnyNavBack())
            Add("n-back", "back", "Nav back", "locus stack");

        // Quality stabilizer: after thick files / gate findings — guide, don't sermon.
        if (quality is { Enabled: true, Fail: > 0 })
            Add("n-quality", "quality", "Quality gates", $"FAIL×{quality.Fail} — harness next step");
        else if (quality is { Enabled: true, Warn: > 0 })
            Add("n-quality", "quality", "Quality gates", $"WARN×{quality.Warn} — review or tune overlay");

        if (quality.SuggestSniper && !EditSniper.HasHold)
            Add("n-scope", "scope", "Sniper aim", "Large open file — aim corridor before thick edit");

        // VS-style: File Modified Outside the Environment — Reload?
        if (buffer.DiskChangedCount > 0)
        {
            Add("n-disk-peek", "disk_peek", "Peek disk vs memory",
                "Glance before Reload? (mtime / content)");
            Add("n-reload", "reload", "Reload from disk",
                $"{buffer.DiskChangedCount} file(s) changed outside — like VS Reload?");
            Add("n-keep-disk", "keep_disk", "Keep memory",
                focusId is { Length: > 0 } && focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
                    ? $"Don't Reload — locus {focusId} → path="
                    : "Don't Reload — silence all drifted (or path= / locus=buffer:…)");
        }

        // Sniper beats (kj-1848): scope → target → shoot — prefer over file-wide outline.
        if (EditSniper.HasHold)
        {
            Add("n-target", "target", "Outline corridor", $"Aim {EditSniper.PulseLine}");
            Add("n-peek", "peek", "Peek aim", "wire= optional; corridor window");
            if (EditorComfort.AnyClipboard())
                Add("n-paste-sniper", "paste_sniper", "Paste frame into aim", "MRU/frame= replace hold");
            Add("n-put-sniper", "put_sniper", "Put draft into aim", "text=/frame= thick rewrite");
            Add("n-edit-draft", "edit_draft", "Shoot (draft)", "mutate/fix inside aim");
            Add("n-scope-clear", "scope_clear", "Clear aim", "drop From/Till");
        }
        else if (buffer.Count > 0 || session.ProjectRoot is not null)
        {
            Add("n-scope", "scope", "Sniper aim", "from=/till= corridor before outline");
            if (session.ProjectRoot is not null)
                Add("n-put", "put", "Put draft file", "path= + text=/frame= — one-shot dump");
            if (buffer.Count > 0)
                Add("n-take", "take", "Take / ship", "verify → chat_markdown (inverse of put)");
        }

        if (buffer.Count > 0 && !EditSniper.HasHold)
            Add("n-edit-draft", "edit_draft", "Edit plan draft", $"Open buffers={buffer.Count} dirty={buffer.DirtyCount}");
        else if (session.ProjectRoot is not null && buffer.Count == 0 && !EditSniper.HasHold)
            Add("n-buffer", "buffer_scene", "Buffer scene", "No open buffers yet");

        if (session.ProjectRoot is not null)
            Add("n-script", "script_scene", "Script habitat", "put→diags→check→run");

        if (gitRoot is { } g && GitIsDirty(g))
            Add("n-git-draft", "git_draft", "Git plan draft", "Dirty SCM — logical slices");
        else
            Add("n-git", "git_scene", "Git scene", "SCM map");

        if (test.Failed > 0)
            Add("n-test-plan", "test_plan", "Retest failed", "last_run has failures");
        else
            Add("n-test", "test_scene", "Test scene", "Discover / last_run");

        if (debug.Stopped)
            Add("n-debug", "debug_scene", "Debug scene", "DAP stopped — stop_context via organ");
        else
            Add("n-shell", "shell_scene", "Shell habitat", shell.Running > 0 ? "jobs running" : "tabs map");

        Add("n-settings", "options", "Tools → Options", "IDE prefs — internet/desk/shell/mcp (not Cursor)");

        if (focusId is { Length: > 0 }
            && focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focusId, "buffer:none", StringComparison.OrdinalIgnoreCase))
            Add("n-focus-editor", "editor_scene", "Focus editor context", $"locus {focusId}");

        if (work.IntentId is not null)
            Add("n-plan", "plan", "Task Manager", work.Pulse ?? work.IntentId);
        else
            Add("n-plan", "plan", "Task Manager", "no plan — feature <name>");

        Add("n-chk", "chk", "Checklists", "mfd=chk");
        return list.ToArray();
    }

    static object SessionPulse(SessionContext session) => new
    {
        phase = CdpEnumParse.ToWire(session.Phase),
        @object = CdpEnumParse.ToWire(session.Object),
        language = session.Language,
        project_root = session.ProjectRoot,
        scm_root = session.ScmRoot,
        solution_or_project_path = session.SolutionOrProjectPath
    };

    static object BuildNavPage(IReadOnlyList<Locus> loci, object? focus) => new
    {
        title = "NAV",
        note = "Pick locus= for detail, or go=<verb> from next[] / locus.go.",
        locus_count = loci.Count,
        focus
    };

    static object BuildSysPage(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work) => new
    {
        title = "SYS",
        project = session.ProjectRoot is null ? "no_project — cdp_open" : session.ProjectRoot,
        git = GitPulseLine(gitRoot),
        shell = $"tabs={shell.TabCount} running={shell.Running} failed={shell.Failed}",
        buffer = $"open={buffer.Count} dirty={buffer.DirtyCount} disk_changed={buffer.DiskChangedCount}",
        debug = debug.ActiveDap
            ? $"dap stopped={debug.Stopped} bp={debug.BreakpointCount}"
            : $"idle bp={debug.BreakpointCount}",
        test = test.Available
            ? test.LastRun is null
                ? "no last_run — cdp_test_scene"
                : $"last {(test.Success ? "ok" : "FAIL")} {test.Passed}/{test.Total}"
            : test.Reason,
        work = work.Pulse ?? "no plan"
    };

    static object BuildChkPage(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test)
    {
        var hasProject = !string.IsNullOrWhiteSpace(session.ProjectRoot);
        var gitDirty = GitIsDirty(gitRoot);
        var testOk = test is { Available: true, LastRun: not null, Success: true };
        var testFail = test is { Available: true, LastRun: not null, Success: false };

        return new
        {
            title = "CHK",
            note = "Living checklists — mark via work, not export ritual.",
            lists = new object[]
            {
                new
                {
                    id = "habitat",
                    title = "Stay in agent IDE",
                    items = new object[]
                    {
                        Item("cdp_open / cockpit before thrash", hasProject),
                        Item("prefer cdp_editor_scene → cdp_edit_plan for multi-step", true),
                        Item("prefer cdp_buffer over Cursor Write", buffer.DirtyCount == 0 || hasProject),
                        Item("cdp_shell_* primary; terminal_* escape only", true),
                        Item("no Cursor Write when buffer plane fits", true)
                    }
                },
                new
                {
                    id = "ship",
                    title = "Ship loop",
                    items = new object[]
                    {
                        Item("tests green (or failed_first plan)", testOk || (!testFail && hasProject)),
                        Item("git dirty understood (scene/plan)", gitRoot is not null),
                        Item("logical commits (git_plan slices)", !gitDirty || gitRoot is not null),
                        Item("push when asked", true)
                    }
                },
                new
                {
                    id = "deploy",
                    title = "Hard deploy recovery",
                    items = new object[]
                    {
                        Item("publish -Mode hard (external; auto CDP_RELOAD_NUDGE)", true),
                        Item("cdp_health version check", true),
                        Item("cdp_cockpit reorient", hasProject)
                    }
                },
                new
                {
                    id = "debug",
                    title = "Debug stop",
                    items = new object[]
                    {
                        Item("stop_context before guess", !debug.Stopped || debug.ActiveDap),
                        Item("debug_stop before rebuild", true)
                    }
                }
            }
        };
    }

    static object Item(string text, bool done) => new { text, done };

    static List<Locus> BuildLoci(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        IdeSettingsHabitat.SettingsPulse settings,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        QualityGates.QualitySnap quality)
    {
        var list = new List<Locus>();

        list.Add(new Locus(
            "session:project",
            "session",
            session.ProjectRoot is null
                ? "no project — cdp_open"
                : $"{session.Language ?? "?"} @ {ShortPath(session.ProjectRoot)}",
            "cdp_open / cdp_session",
            "project_scene",
            SessionPulse(session)));

        list.Add(new Locus(
            "settings:ide",
            "settings",
            settings.Line,
            "go=options → page=internet|desk|shell|mcp",
            "settings",
            new
            {
                ok = settings.Ok,
                user_count = settings.UserCount,
                user_path = settings.UserPath,
                process_path = settings.ProcessPath
            }));

        if (gitRoot is { } g)
        {
            var dirty = GitIsDirty(g);
            var branch = FirstGitBranch(g) ?? "?";
            list.Add(new Locus(
                "git:scm",
                "git",
                dirty ? $"dirty on {branch}" : $"clean {branch}",
                "go=git_scene → go=git_draft",
                dirty ? "git_draft" : "git_scene",
                CompactGit(g)));
        }
        else
        {
            list.Add(new Locus(
                "git:scm",
                "git",
                "unavailable — cdp_open scm_root",
                "go=git_scene",
                "git_scene",
                new { available = false }));
        }

        foreach (var tab in shell.Tabs.Take(12))
        {
            var id = $"shell:{tab.Id}";
            var pulse = $"{tab.State}" +
                        (tab.LastExit is { } ex ? $" exit={ex}" : "") +
                        (tab.Cwd is { } cwd ? $" @ {ShortPath(cwd)}" : "");
            list.Add(new Locus(
                id,
                "shell",
                pulse,
                "go=shell_scene / go=shell_last",
                "shell_scene",
                tab));
        }

        list.Add(new Locus(
            "browser:net",
            "browser",
            browser.Line,
            "go=browser / go=search q=… / layout=code+net",
            "browser",
            new
            {
                ok = browser.Ok,
                active_tab = browser.ActiveTab,
                tab_count = browser.TabCount,
                url = browser.Url,
                preview = browser.Preview,
                lynx = browser.LynxPath
            }));

        foreach (var doc in buffer.Docs.Take(16))
        {
            var both = doc.DiskChanged && doc.Dirty;
            var pulse =
                (both ? "DIRTY+DISK " : doc.DiskChanged ? "DISK CHANGED " : doc.Dirty ? "DIRTY " : "") +
                ShortPath(doc.Path);
            list.Add(new Locus(
                $"buffer:{doc.DocId}",
                "buffer",
                pulse,
                doc.DiskChanged
                    ? (both
                        ? "go=disk_peek → reload loses edits; or keep_disk"
                        : "go=disk_peek → reload | keep_disk — modified outside")
                    : "go=editor_scene → go=edit_draft",
                doc.DiskChanged ? "disk_peek" : "editor_scene",
                doc));
        }

        if (buffer.Count == 0)
        {
            list.Add(new Locus(
                "buffer:none",
                "buffer",
                "no open buffers",
                "cdp_buffer op=open → go=editor_scene",
                "buffer_scene",
                new { count = 0 }));
        }

        if (EditorComfort.ClipboardLocusDetail() is { } clip)
        {
            list.Add(new Locus(
                "clip:session",
                "clipboard",
                $"clip ×{clip.Count} ({clip.CurrentId})",
                "go=clipboard → paste frame= | clip_clear",
                "clipboard",
                new
                {
                    count = clip.Count,
                    current = clip.CurrentId,
                    chars = clip.Chars,
                    from = clip.From,
                    preview = clip.Preview
                }));
        }

        list.Add(new Locus(
            "debug:session",
            "debug",
            debug.ActiveDap
                ? (debug.Stopped ? "STOPPED" : "dap running") + $" bp={debug.BreakpointCount}"
                : $"idle bp={debug.BreakpointCount}",
            "go=debug_scene",
            "debug_scene",
            debug));

        list.Add(new Locus(
            "test:last",
            "test",
            !test.Available
                ? test.Reason ?? "unavailable"
                : test.LastRun is null
                    ? "no last_run"
                    : $"{(test.Success ? "ok" : "FAIL")} {test.Passed}/{test.Total}",
            test.Failed > 0 ? "go=test_plan" : "go=test_scene",
            test.Failed > 0 ? "test_plan" : "test_scene",
            test));

        list.Add(new Locus(
            "analysis:scene",
            "analysis",
            session.ProjectRoot is { Length: > 0 } ? "analysis ready" : "no project",
            "go=analysis_scene → correspondence|semantic_map|clones",
            "analysis_scene",
            new { features = new[] { "correspondence", "semantic_map", "clones" } }));

        list.Add(new Locus(
            "plan:focus",
            "plan",
            work.Pulse ?? "no plan — feature <name>",
            "go=plan / cmd=\"feature X\" | task Y | done",
            "plan",
            work));

        list.Add(new Locus(
            "mfd:chk",
            "mfd",
            "checklists (ship/deploy/habitat)",
            "go=chk",
            "chk",
            new { switch_to = "chk" }));

        if (quality.Enabled)
        {
            list.Add(new Locus(
                "mfd:gates",
                "mfd",
                quality.Fail > 0 || quality.Warn > 0
                    ? $"quality {quality.Pulse}"
                    : "quality gates ok",
                "go=quality / mfd=gates — project-tunable",
                "quality",
                quality));
        }

        return list;
    }

    static async Task<JsonElement?> TryGitAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        bool includeSubmodules,
        CancellationToken cancellationToken)
    {
        if (!byDomain.TryGetValue(CdpDomains.Git, out var git) || !git.IsEnabled)
            return null;

        var root = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var callArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["workspace_path"] = JsonSerializer.SerializeToElement(root),
                ["include_submodules"] = JsonSerializer.SerializeToElement(includeSubmodules),
                ["max_roots"] = JsonSerializer.SerializeToElement(4)
            };
            var raw = await git.CallAsync("git_scene", callArgs).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    static object CompactGit(JsonElement root)
    {
        var roots = new List<object>();
        if (root.TryGetProperty("roots", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray().Take(8))
            {
                roots.Add(new
                {
                    path = PropStr(r, "path"),
                    ok = PropBool(r, "ok"),
                    branch = PropStr(r, "branch"),
                    dirty = PropBool(r, "dirty"),
                    ahead = PropInt(r, "ahead"),
                    behind = PropInt(r, "behind"),
                    counts = r.TryGetProperty("counts", out var c)
                        ? JsonSerializer.Deserialize<object>(c.GetRawText())
                        : null
                });
            }
        }

        return new { schema = "git_scene/v0", roots };
    }

    static bool GitIsDirty(JsonElement? root)
    {
        if (root is not { } g)
            return false;
        if (!g.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var r in arr.EnumerateArray())
        {
            if (PropBool(r, "dirty") == true)
                return true;
        }

        return false;
    }

    static string? FirstGitBranch(JsonElement root)
    {
        if (!root.TryGetProperty("roots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var r in arr.EnumerateArray())
        {
            var b = PropStr(r, "branch");
            if (b is { Length: > 0 })
                return b;
        }

        return null;
    }

    static string GitPulseLine(JsonElement? root)
    {
        if (root is null)
            return "n/a";
        var branch = FirstGitBranch(root.Value) ?? "?";
        return GitIsDirty(root) ? $"dirty ({branch})" : $"clean ({branch})";
    }

    sealed record ShellTab(string Id, string State, string? Cwd, int? LastExit, string? LastCommand);

    sealed record ShellSnap(int TabCount, int Running, int Failed, IReadOnlyList<ShellTab> Tabs);

    static ShellSnap CollectShell(string sceneJson)
    {
        using var doc = JsonDocument.Parse(sceneJson);
        var root = doc.RootElement;
        var tabs = new List<ShellTab>();
        var running = 0;
        var failed = 0;
        if (root.TryGetProperty("tabs", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                var state = PropStr(t, "state") ?? "unknown";
                if (string.Equals(state, "running", StringComparison.OrdinalIgnoreCase))
                    running++;
                if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
                    failed++;
                tabs.Add(new ShellTab(
                    PropStr(t, "id") ?? "?",
                    state,
                    PropStr(t, "cwd"),
                    PropInt(t, "last_exit"),
                    Truncate(PropStr(t, "last_command"), 80)));
            }
        }

        return new ShellSnap(PropInt(root, "tab_count") ?? tabs.Count, running, failed, tabs);
    }

    sealed record BufferDoc(
        string DocId,
        string Path,
        string? Language,
        bool Dirty,
        bool DiskChanged,
        int? Version);

    sealed record BufferSnap(int Count, int DirtyCount, int DiskChangedCount, IReadOnlyList<BufferDoc> Docs);

    static BufferSnap CollectBuffer(object sceneObj)
    {
        var json = JsonSerializer.Serialize(sceneObj, Compact);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var docs = new List<BufferDoc>();
        if (root.TryGetProperty("docs", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in arr.EnumerateArray())
            {
                docs.Add(new BufferDoc(
                    PropStr(d, "doc_id") ?? "?",
                    PropStr(d, "path") ?? "?",
                    PropStr(d, "language"),
                    PropBool(d, "dirty") == true,
                    PropBool(d, "disk_changed") == true,
                    PropInt(d, "version")));
            }
        }

        return new BufferSnap(
            PropInt(root, "count") ?? docs.Count,
            PropInt(root, "dirty_count") ?? docs.Count(d => d.Dirty),
            PropInt(root, "disk_changed_count") ?? docs.Count(d => d.DiskChanged),
            docs);
    }

    sealed record DebugSnap(bool ActiveDap, bool Stopped, int LastStoppedThreadId, int BreakpointCount);

    static DebugSnap CollectDebug(SessionContext session)
    {
        var ws = session.ProjectRoot ?? session.ScmRoot;
        var target = session.SolutionOrProjectPath;
        var bpCount = 0;
        if (!string.IsNullOrWhiteSpace(ws) && !string.IsNullOrWhiteSpace(target))
        {
            try
            {
                bpCount = BreakpointsStorage.GetBreakpoints(ws, target).Count;
            }
            catch
            {
                /* ignore */
            }
        }

        return new DebugSnap(
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            DebugSession.LastStoppedThreadId,
            bpCount);
    }

    sealed record TestSnap(
        bool Available,
        string? Reason,
        string? Target,
        bool? LastRun,
        bool Success,
        int Total,
        int Passed,
        int Failed,
        object? Detail);

    static TestSnap CollectTest(SessionContext session)
    {
        if (!IdeSessionLifecycle.TryResolveTarget(session, new Dictionary<string, JsonElement>(), out var target, out var err))
            return new TestSnap(false, err, null, null, false, 0, 0, 0, null);

        var last = TestRunCache.TryGet(target);
        if (last is null)
            return new TestSnap(true, null, target, null, false, 0, 0, 0, new { target, last_run = (object?)null });

        return new TestSnap(
            true,
            null,
            target,
            true,
            last.Success,
            last.Total,
            last.Passed,
            last.Failed,
            new
            {
                target,
                at_utc = last.AtUtc,
                success = last.Success,
                total = last.Total,
                passed = last.Passed,
                failed = last.Failed,
                skipped = last.Skipped,
                filter = last.Filter,
                failed_names = last.FailedTests.Select(f => f.Name).Take(12).ToArray()
            });
    }

    sealed record WorkSnap(string? IntentId, string? StageId, string? Pulse);

    static WorkSnap CollectWork(IntentWorkspaceStore? store, IntentWorkspaceState state)
    {
        if (store is null)
            return new WorkSnap(null, null, "no task store");
        var pulse = IdeTaskManager.PulseLine(store, state);
        return new WorkSnap(
            state.ActiveIntentId?.ToString("D"),
            state.ActiveStageId?.ToString("D"),
            pulse);
    }

    static string ShortPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.IsNullOrEmpty(name))
                return path;
            return string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
        }
        catch
        {
            return path;
        }
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static string? PropStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static bool? PropBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
            ? p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    static int? PropInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        return null;
    }

    static string? Truncate(string? s, int max)
    {
        if (s is null)
            return null;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
