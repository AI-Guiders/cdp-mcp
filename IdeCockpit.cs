using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE cockpit hub — compact where-am-I map (kj-1329).
/// Drill-down stays existing *_scene tools; does not replace <c>cdp_session</c> pack dogfood.
/// </summary>
internal static class IdeCockpit
{
    public const string SchemaVersion = "cockpit/v0";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static async Task<string> BuildAsync(
        SessionContext session,
        DocumentBufferStore docStore,
        ShellHabitat shellHabitat,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var includeGit = BoolOr(args, "include_git", true);
        var includeShell = BoolOr(args, "include_shell", true);
        var includeBuffer = BoolOr(args, "include_buffer", true);
        var includeDebug = BoolOr(args, "include_debug", true);
        var includeTest = BoolOr(args, "include_test", true);
        var includeWork = BoolOr(args, "include_work", true);
        var includeSubmodules = BoolOr(args, "include_submodules", false);

        object? gitPanel = null;
        if (includeGit)
            gitPanel = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);

        object? shellPanel = null;
        if (includeShell)
            shellPanel = CompactShell(shellHabitat.Scene());

        object? bufferPanel = null;
        if (includeBuffer)
            bufferPanel = CompactBuffer(docStore.Scene());

        object? debugPanel = null;
        if (includeDebug)
            debugPanel = CompactDebug(session);

        object? testPanel = null;
        if (includeTest)
            testPanel = CompactTest(session);

        object? workPanel = null;
        if (includeWork && workspaceStore is not null)
        {
            var (wid, sid, sname, _) = workspaceStore.PlaneIds(workspaceState);
            workPanel = new
            {
                active_intent_id = wid,
                active_scene_id = sid,
                active_scene_name = sname,
                drill = "cdp_work op=status"
            };
        }

        var payload = new
        {
            schema = SchemaVersion,
            ok = true,
            session = new
            {
                phase = CdpEnumParse.ToWire(session.Phase),
                @object = CdpEnumParse.ToWire(session.Object),
                intent = session.Intent is { } i ? CdpEnumParse.ToWire(i) : null,
                language = session.Language,
                project_root = session.ProjectRoot,
                project_kind = session.ProjectKind,
                solution_or_project_path = session.SolutionOrProjectPath,
                scm_root = session.ScmRoot,
                tsconfig_path = session.TsConfigPath
            },
            shell = shellPanel,
            buffer = bufferPanel,
            debug = debugPanel,
            git = gitPanel,
            test = testPanel,
            work = workPanel,
            drill = new
            {
                session = "cdp_session",
                shell = "cdp_shell_scene",
                buffer = "cdp_buffer op=scene",
                debug = "cdp_debug op=scene",
                git = "git_git_scene",
                test = "cdp_test_scene",
                project = "cdp_project_scene",
                work = "cdp_work op=status",
                hint = "Cockpit = hub; drill *_scene for full maps. Prefer cdp_cockpit after hard deploy / cdp_open before thrash."
            }
        };

        return JsonSerializer.Serialize(payload, Pretty);
    }

    static async Task<object> TryGitAsync(
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        bool includeSubmodules,
        CancellationToken cancellationToken)
    {
        if (!byDomain.TryGetValue(CdpDomains.Git, out var git) || !git.IsEnabled)
            return new { available = false, reason = "git_backend_disabled" };

        var root = session.ScmRoot ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            return new { available = false, reason = "no_scm_root_call_cdp_open" };

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
            return CompactGit(doc.RootElement);
        }
        catch (Exception ex)
        {
            return new { available = true, ok = false, error = Truncate(ex.Message, 240) };
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
                    kind = PropStr(r, "kind"),
                    branch = PropStr(r, "branch"),
                    dirty = PropBool(r, "dirty"),
                    ahead = PropInt(r, "ahead"),
                    behind = PropInt(r, "behind"),
                    counts = r.TryGetProperty("counts", out var c) ? JsonSerializer.Deserialize<object>(c.GetRawText()) : null
                });
            }
        }

        return new
        {
            available = true,
            ok = PropBool(root, "ok") ?? true,
            schema = PropStr(root, "schema") ?? "git_scene/v0",
            roots,
            drill = "git_git_scene"
        };
    }

    static object CompactShell(string sceneJson)
    {
        using var doc = JsonDocument.Parse(sceneJson);
        var root = doc.RootElement;
        var tabs = new List<object>();
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
                tabs.Add(new
                {
                    id = PropStr(t, "id"),
                    state,
                    cwd = PropStr(t, "cwd"),
                    last_exit = PropInt(t, "last_exit"),
                    last_command = Truncate(PropStr(t, "last_command"), 80)
                });
            }
        }

        return new
        {
            tab_count = PropInt(root, "tab_count") ?? tabs.Count,
            running,
            failed,
            tabs,
            drill = "cdp_shell_scene"
        };
    }

    static object CompactBuffer(object sceneObj)
    {
        var json = JsonSerializer.Serialize(sceneObj, Compact);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var dirty = new List<object>();
        var count = PropInt(root, "count") ?? 0;
        if (root.TryGetProperty("docs", out var docs) && docs.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in docs.EnumerateArray())
            {
                if (PropBool(d, "dirty") != true)
                    continue;
                dirty.Add(new
                {
                    doc_id = PropStr(d, "doc_id"),
                    path = PropStr(d, "path"),
                    language = PropStr(d, "language"),
                    version = PropInt(d, "version")
                });
            }
        }

        return new
        {
            count,
            dirty_count = dirty.Count,
            dirty,
            drill = "cdp_buffer op=scene"
        };
    }

    static object CompactDebug(SessionContext session)
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

        var stopped = DebugSession.LastStoppedThreadId > 0;
        return new
        {
            active_dap = DebugSession.CurrentClient is not null,
            stopped,
            last_stopped_thread_id = DebugSession.LastStoppedThreadId,
            breakpoint_count = bpCount,
            drill = stopped ? "cdp_debug op=stop_context" : "cdp_debug op=scene"
        };
    }

    static object CompactTest(SessionContext session)
    {
        if (!IdeSessionLifecycle.TryResolveTarget(session, new Dictionary<string, JsonElement>(), out var target, out var err))
            return new { available = false, reason = err, drill = "cdp_test_scene" };

        var last = TestRunCache.TryGet(target);
        if (last is null)
            return new { available = true, target, last_run = (object?)null, drill = "cdp_test_scene" };

        return new
        {
            available = true,
            target,
            last_run = new
            {
                at_utc = last.AtUtc,
                success = last.Success,
                total = last.Total,
                passed = last.Passed,
                failed = last.Failed,
                skipped = last.Skipped,
                filter = last.Filter,
                failed_names = last.FailedTests.Select(f => f.Name).Take(12).ToArray()
            },
            drill = last.Failed > 0 ? "cdp_test_plan failed_first=true" : "cdp_test_scene"
        };
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
