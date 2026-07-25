using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE cockpit — single-screen MFD + loci + desk dispatcher (kj-1329 / kj-1603 / kj-1721).
/// Modes: nav | sys | chk. <c>locus=</c> for detail; <c>go=</c> routes to organs (not a monolith).
/// </summary>
internal static class IdeCockpit
{
    public const string SchemaVersion = "cockpit/v1.2";
    public const int GoResultCapChars = 24_000;
    public const int GoPulseCapChars = 1_200;

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
            ["q"] = ("cdp_goto", Dict(("kind", "feature"))),
            ["feature"] = ("cdp_goto", Dict(("kind", "feature"))),
            ["shell_scene"] = ("cdp_shell_scene", null),
            ["shell"] = ("cdp_shell_scene", null),
            ["shell_last"] = ("cdp_shell_last", null),
            ["debug_scene"] = ("cdp_debug", Dict(("op", "scene"))),
            ["debug"] = ("cdp_debug", Dict(("op", "scene"))),
            ["build"] = ("cdp_build", null),
            ["project_scene"] = ("cdp_project_scene", null),
            ["project"] = ("cdp_project_scene", null),
            ["work"] = ("cdp_work", Dict(("op", "status"))),
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
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var mfd = OptString(args, "mfd") ?? OptString(args, "page") ?? "nav";
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

        object? goResult = null;
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
            goVerb = null;
        }

        if (goVerb is { Length: > 0 })
        {
            goResult = await DispatchGoAsync(goVerb.Trim(), args, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
            // Re-collect after organ may have mutated buffers (reload/keep_disk/edit…).
            buffer = CollectBuffer(docStore.Scene());
        }

        var git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
        var shell = CollectShell(shellHabitat.Scene());
        var debug = CollectDebug(session);
        var test = CollectTest(session);
        var work = CollectWork(workspaceStore, workspaceState);
        var quality = QualityGates.Snap(docStore, session.ProjectRoot);

        var loci = BuildLoci(session, git, shell, buffer, debug, test, work, quality);
        var next = BuildNext(session, git, shell, buffer, debug, test, work, focusId, quality);

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
            .Concat(["quality", "gates"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var payload = new
        {
            schema = SchemaVersion,
            ok = true,
            role = "desk",
            mfd,
            mfd_pages = new[] { "nav", "sys", "chk", "gates" },
            session = SessionPulse(session),
            loci = loci.Select(l => l.Card()).ToArray(),
            next,
            focus,
            page,
            go = goResult,
            go_verbs = goVerbs,
            hint =
                "Cold start: cdp_cockpit first. Desk: mfd=|locus=|go= (default go_detail=pulse). " +
                "locus=buffer:doc-N scopes go=disk_peek|reload|keep_disk to that file. " +
                "Edit sniper: go=scope from=/till= → go=target → go=peek → go=edit_draft. " +
                "Quality: go=quality / mfd=gates (project-tunable .cdp/quality-gates.toml). " +
                "Analysis: go=analysis_scene / go=correspondence|semantic_map|clones (domain scene, not MFD). " +
                "Scripts: go=script_scene / go=script_put|check|run (put→diags→run, not pray). " +
                "Editor comfort: go=undo|redo|copy|cut|paste|put|clipboard|find|…. " +
                "put: dump draft (path=|sniper) text=/frame= then refine. " +
                "Clipboard frames; paste_sniper/put_sniper into aim. " +
                "Find: go=find / find_in_files; Go To: go=goto. " +
                "go_detail=full for organ dump. Organs stay — not a monolith."
        };

        return JsonSerializer.Serialize(payload, Pretty);
    }

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

    sealed record OrganPulse(bool Ok, string Line, string? Schema, object? Next, string? Hint);

    static OrganPulse PulseFromOrgan(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
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
        QualityGates.QualitySnap quality)
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
            Add("n-open", "project_scene", "Project map", "No project — cdp_open / project_scene first");
        else
        {
            Add("n-goto", "goto", "Go To (Ctrl+T)", "query= type/member/file — land on anchor");
            Add("n-editor", "editor_scene", "Editor map", "Buffer/desk loop");
        }

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

        if (focusId is { Length: > 0 }
            && focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focusId, "buffer:none", StringComparison.OrdinalIgnoreCase))
            Add("n-focus-editor", "editor_scene", "Focus editor context", $"locus {focusId}");

        if (work.IntentId is not null)
            Add("n-work", "work", "Work status", work.SceneName ?? work.IntentId);

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
        work = work.IntentId is null ? "no active intent" : $"intent={work.IntentId} scene={work.SceneName}"
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
                        Item("publish -Mode hard from external terminal", true),
                        Item("mcp.json CDP_RELOAD_NUDGE (kj-1349)", true),
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
            "work:focus",
            "work",
            work.IntentId is null ? "no active intent" : $"{work.SceneName ?? work.IntentId}",
            "go=work",
            "work",
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

    sealed record WorkSnap(string? IntentId, string? SceneId, string? SceneName);

    static WorkSnap CollectWork(IntentWorkspaceStore? store, IntentWorkspaceState state)
    {
        if (store is null)
            return new WorkSnap(null, null, null);
        var (wid, sid, sname, _) = store.PlaneIds(state);
        return new WorkSnap(wid, sid, sname);
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
