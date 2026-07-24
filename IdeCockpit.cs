using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE cockpit — single-screen MFD + loci navigation (kj-1329 / kj-1603).
/// Modes: nav | sys | chk. Select <c>locus=</c> for detail (CodeAnchor-like). Not a CIDE multi-pane clone.
/// </summary>
internal static class IdeCockpit
{
    public const string SchemaVersion = "cockpit/v1";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    static readonly HashSet<string> MfdPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "sys", "chk"
    };

    sealed class Locus(
        string Id,
        string Kind,
        string Pulse,
        string Drill,
        object? Detail = null)
    {
        public string Id { get; } = Id;
        public string Kind { get; } = Kind;
        public string Pulse { get; } = Pulse;
        public string Drill { get; } = Drill;
        public object? Detail { get; } = Detail;

        public object Card() => new
        {
            id = Id,
            kind = Kind,
            pulse = Pulse,
            drill = Drill
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
        CancellationToken cancellationToken)
    {
        var mfd = OptString(args, "mfd") ?? OptString(args, "page") ?? "nav";
        mfd = mfd.Trim().ToLowerInvariant();
        if (!MfdPages.Contains(mfd))
            mfd = "nav";

        var focusId = OptString(args, "locus") ?? OptString(args, "focus");
        var includeSubmodules = BoolOr(args, "include_submodules", false);

        var git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
        var shell = CollectShell(shellHabitat.Scene());
        var buffer = CollectBuffer(docStore.Scene());
        var debug = CollectDebug(session);
        var test = CollectTest(session);
        var work = CollectWork(workspaceStore, workspaceState);

        var loci = BuildLoci(session, git, shell, buffer, debug, test, work);

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
                    detail = hit.Detail
                };
        }

        object? page = mfd switch
        {
            "sys" => BuildSysPage(session, git, shell, buffer, debug, test, work),
            "chk" => BuildChkPage(session, git, shell, buffer, debug, test),
            _ => BuildNavPage(loci, focus)
        };

        var payload = new
        {
            schema = SchemaVersion,
            ok = true,
            mfd,
            mfd_pages = new[] { "nav", "sys", "chk" },
            session = SessionPulse(session),
            loci = loci.Select(l => l.Card()).ToArray(),
            focus,
            page,
            hint =
                "Single-screen MFD: mfd=nav|sys|chk; locus=<id> for detail (CodeAnchor-like). " +
                "Not multi-pane. Drill via locus.drill / existing *_scene."
        };

        return JsonSerializer.Serialize(payload, Pretty);
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
        note = "Pick locus= to open detail on this screen.",
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
        buffer = $"open={buffer.Count} dirty={buffer.DirtyCount}",
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
        WorkSnap work)
    {
        var list = new List<Locus>();

        list.Add(new Locus(
            "session:project",
            "session",
            session.ProjectRoot is null
                ? "no project — cdp_open"
                : $"{session.Language ?? "?"} @ {ShortPath(session.ProjectRoot)}",
            "cdp_open / cdp_session",
            SessionPulse(session)));

        if (gitRoot is { } g)
        {
            var dirty = GitIsDirty(g);
            var branch = FirstGitBranch(g) ?? "?";
            list.Add(new Locus(
                "git:scm",
                "git",
                dirty ? $"dirty on {branch}" : $"clean {branch}",
                "git_git_scene → git_git_plan",
                CompactGit(g)));
        }
        else
        {
            list.Add(new Locus(
                "git:scm",
                "git",
                "unavailable — cdp_open scm_root",
                "git_git_scene",
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
                "cdp_shell_scene / cdp_shell_last",
                tab));
        }

        foreach (var doc in buffer.Docs.Take(16))
        {
            list.Add(new Locus(
                $"buffer:{doc.DocId}",
                "buffer",
                (doc.Dirty ? "DIRTY " : "") + ShortPath(doc.Path),
                "cdp_editor_scene path=… → cdp_edit_plan",
                doc));
        }

        if (buffer.Count == 0)
        {
            list.Add(new Locus(
                "buffer:none",
                "buffer",
                "no open buffers",
                "cdp_buffer op=open → cdp_editor_scene",
                new { count = 0 }));
        }

        list.Add(new Locus(
            "debug:session",
            "debug",
            debug.ActiveDap
                ? (debug.Stopped ? "STOPPED" : "dap running") + $" bp={debug.BreakpointCount}"
                : $"idle bp={debug.BreakpointCount}",
            debug.Stopped ? "cdp_debug op=stop_context" : "cdp_debug op=scene",
            debug));

        list.Add(new Locus(
            "test:last",
            "test",
            !test.Available
                ? test.Reason ?? "unavailable"
                : test.LastRun is null
                    ? "no last_run"
                    : $"{(test.Success ? "ok" : "FAIL")} {test.Passed}/{test.Total}",
            test.Failed > 0 ? "cdp_test_plan failed_first=true" : "cdp_test_scene",
            test));

        list.Add(new Locus(
            "work:focus",
            "work",
            work.IntentId is null ? "no active intent" : $"{work.SceneName ?? work.IntentId}",
            "cdp_work op=status",
            work));

        list.Add(new Locus(
            "mfd:chk",
            "mfd",
            "checklists (ship/deploy/habitat)",
            "cdp_cockpit mfd=chk",
            new { switch_to = "chk" }));

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

    sealed record BufferDoc(string DocId, string Path, string? Language, bool Dirty, int? Version);

    sealed record BufferSnap(int Count, int DirtyCount, IReadOnlyList<BufferDoc> Docs);

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
                    PropInt(d, "version")));
            }
        }

        return new BufferSnap(
            PropInt(root, "count") ?? docs.Count,
            docs.Count(d => d.Dirty),
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
