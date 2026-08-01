#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static async Task<string?> HubAsync(
        MetaDispatchDeps d,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var byDomain = d.ByDomain;
        var modules = d.Modules;
        var allAffordances = d.AllAffordances;
        var settings = d.Settings;
        var Pretty = d.Pretty;
        var shellHabitat = d.ShellHabitat;
        var mcpOutlet = d.McpOutlet;
        var internetBrowser = d.InternetBrowser;
        var ideSettings = d.IdeSettings;
        var workspaceStore = d.WorkspaceStore;
        var workspaceState = d.WorkspaceState;
        var workspaceDbPath = d.WorkspaceDbPath;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureOpenRecentWired = d.EnsureOpenRecentWired;
        var EnsureWorkspaceDb = d.EnsureWorkspaceDb;
        var DispatchAsync = d.DispatchToolAsync;
        var DispatchCdpWork = d.DispatchCdpWork;

        switch (name)
        {
    case "cdp_tools":
    {
        var qPhase = session.Phase;
        var qObj = session.Object;
        CdpIntent? qIntent = session.Intent;
        string? qLang = session.Language;
        if (callArgs.TryGetValue("phase", out var p2) && CdpEnumParse.TryParsePhase(p2.GetString(), out var pp))
            qPhase = pp;
        if (callArgs.TryGetValue("object", out var o2) && CdpEnumParse.TryParseObject(o2.GetString(), out var oo))
            qObj = oo;
        if (callArgs.TryGetValue("intent", out var i2) && CdpEnumParse.TryParseIntent(i2.GetString(), out var ii))
            qIntent = ii;
        if (callArgs.TryGetValue("language", out var l2) && settings.Languages.TryNormalize(l2.GetString(), out var ll))
            qLang = CdpLanguages.IsAny(ll) ? null : ll;
        var limit = PhaseObjectCatalog.DefaultListToolsLimit;
        if (callArgs.TryGetValue("limit", out var lim) && lim.TryGetInt32(out var li))
            limit = li;
        var hits = PhaseObjectCatalog.Query(allAffordances, qPhase, qObj, qIntent, limit, qLang);
        return JsonSerializer.Serialize(new
        {
            phase = CdpEnumParse.ToWire(qPhase),
            @object = CdpEnumParse.ToWire(qObj),
            intent = qIntent is null ? null : CdpEnumParse.ToWire(qIntent.Value),
            language = qLang,
            total = hits.Count,
            tools = hits.Select(h => new
            {
                name = h.Affordance.PrefixedName,
                score = h.Score,
                cost = h.Affordance.Cost,
                risk = h.Affordance.Risk,
                hint = h.Affordance.Hint
            })
        }, Pretty);
    }
    case "cdp_cockpit":
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWorkspaceDb(); // desk_seats + script_last_run (WitDB)

        return await IdeCockpit.BuildAsync(
                session,
                docStore,
                shellHabitat,
                internetBrowser,
                ideSettings,
                mcpOutlet,
                byDomain,
                workspaceStore,
                workspaceState,
                callArgs,
                DispatchAsync,
                cancellationToken,
                warm)
            .ConfigureAwait(false);
    }
    case "cdp_session":
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shortlistLimit = 12;
        if (callArgs.TryGetValue("shortlist_limit", out var sl) && sl.TryGetInt32(out var sli))
            shortlistLimit = sli;
        var (wid, sid, sname, dbPath) = (null as string, null as string, null as string, workspaceDbPath);
        if (workspaceStore is not null)
            (wid, sid, sname, dbPath) = workspaceStore.PlaneIds(workspaceState);
        var workspacePlane = new WorkspacePlaneDto
        {
            ActiveIntentId = wid,
            ActiveSceneId = sid,
            ActiveSceneName = sname,
            DatabasePath = dbPath
        };
        var plane = await SessionPlane.BuildSessionAsync(
            session, modules, byDomain, allAffordances, callArgs, shortlistLimit, workspacePlane).ConfigureAwait(false);
        return JsonSerializer.Serialize(plane, Pretty);
    }
    case "cdp_work":
    {
        // Escape hatch: Cursor host may omit standalone cdp_buffer / cdp_debug from ListTools;
        // buffer_* and debug_* ops ride on already-advertised cdp_work.
        string? workOp = null;
        if (callArgs.TryGetValue("op", out var workOpEl))
        {
            workOp = workOpEl.ValueKind == JsonValueKind.String
                ? workOpEl.GetString()
                : workOpEl.ToString();
        }

        if (workOp is { Length: > 0 }
            && workOp.Trim().StartsWith("buffer_", StringComparison.OrdinalIgnoreCase))
        {
            var sub = workOp.Trim()["buffer_".Length..].Trim().ToLowerInvariant();
            var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in callArgs)
                mapped[kv.Key] = kv.Value;
            mapped["op"] = JsonSerializer.SerializeToElement(sub);
            return await DocumentEditPlane
                .DispatchAsync("cdp_buffer", docStore, session, byDomain, mapped, cancellationToken)
                .ConfigureAwait(false);
        }

        if (workOp is { Length: > 0 }
            && workOp.Trim().StartsWith("debug_", StringComparison.OrdinalIgnoreCase))
        {
            var sub = workOp.Trim()["debug_".Length..].Trim().ToLowerInvariant();
            var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var kv in callArgs)
                mapped[kv.Key] = kv.Value;
            mapped["op"] = JsonSerializer.SerializeToElement(sub);
            return await DebugPlane
                .DispatchAsync(session, byDomain, mapped, cancellationToken)
                .ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(DispatchCdpWork(callArgs), Pretty);
    }
    case "cdp_csx_check":
    {
        var code = await ResolveCsxSourceAsync(session, callArgs).ConfigureAwait(false);
        var report = await ScriptHost.CheckAsync(code, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(report, Pretty);
    }
    case "cdp_csx_help":
    {
        var op = callArgs.TryGetValue("op", out var opEl) && opEl.GetString() is { Length: > 0 } ops
            ? ops.Trim()
            : "toc";
        var max = callArgs.TryGetValue("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number
            ? maxEl.GetInt32()
            : (int?)null;
        if (op.Equals("toc", StringComparison.OrdinalIgnoreCase))
            return CsxHelpCatalog.Toc(max ?? 48);
        if (op.Equals("of", StringComparison.OrdinalIgnoreCase))
        {
            var path = callArgs.TryGetValue("path", out var pEl) ? pEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path required for cdp_csx_help op=of (e.g. Symbol or SemanticMap.Explore).");
            return CsxHelpCatalog.Of(path!, max ?? 40);
        }

        throw new ArgumentException("op must be toc|of");
    }
    case "cdp_evidence":
    {
        var kind = callArgs.TryGetValue("kind", out var kEl) && kEl.GetString() is { Length: > 0 } ks
            ? ks.Trim()
            : "auto";
        string? text = callArgs.TryGetValue("text", out var tEl) ? tEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(text)
            && callArgs.TryGetValue("path", out var epEl)
            && epEl.GetString() is { Length: > 0 } ep)
        {
            text = await File.ReadAllTextAsync(Path.GetFullPath(ep), cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("text or path required for cdp_evidence");

        var ctx = new EvidenceContext(
            ProjectRoot: session.ProjectRoot,
            SolutionOrProjectPath: session.SolutionOrProjectPath);
        return EvidencePreprocess.ToJson(EvidencePreprocess.Project(kind, text, ctx));
    }
    case "cdp_csx_run":
    {
        var code = await ResolveCsxSourceAsync(session, callArgs).ConfigureAwait(false);
        var mode = callArgs.TryGetValue("mode", out var mEl) && mEl.GetString() is { Length: > 0 } ms
            ? ms.Trim()
            : "run";
        var dry = mode.Equals("dry_run", StringComparison.OrdinalIgnoreCase)
                  || mode.Equals("dryRun", StringComparison.OrdinalIgnoreCase);
        var root = callArgs.TryGetValue("workspace_path", out var wp) && wp.GetString() is { Length: > 0 } wps
            ? Path.GetFullPath(wps)
            : session.ProjectRoot is { Length: > 0 } pr ? pr : Environment.CurrentDirectory;
        var plan = new PlanContext
        {
            PrimaryRoot = root,
            WorkRoot = root,
            PlanId = "",
            SolutionOrProjectPath = session.SolutionOrProjectPath,
            Language = session.Language
        };
        ProjectSettingsLoader.Hydrate(plan);
        var bus = new ScriptToolBus(async (domain, underlying, args, ct) =>
        {
            if (string.Equals(domain, "cdp", StringComparison.Ordinal)
                && string.Equals(underlying, "session_open", StringComparison.Ordinal))
            {
                EnsureOpenRecentWired();
                var path = args.TryGetValue("path", out var pEl) ? pEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("path required for cdp.session_open");
                var open = settings.Languages.Detect(path!);
                var park = docStore.ParkOutsideProject(open.Root);
                var payload = IdeLanguageTools.ApplyOpen(session, open, park);
                // Keep Plan in sync with session for rest of this CSX.
                plan.Rebind(
                    open.Root,
                    open.SolutionOrProjectPath ?? open.TsConfigPath,
                    CdpLanguages.IsAny(open.Language) ? null : open.Language);
                NotifyListChanged();
                return payload;
            }

            if (string.Equals(domain, "cdp_work", StringComparison.Ordinal))
            {
                var mapped = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal)
                {
                    ["op"] = JsonSerializer.SerializeToElement(underlying)
                };
                var result = DispatchCdpWork(mapped);
                return result is string s
                    ? s
                    : JsonSerializer.Serialize(result, Pretty);
            }

            if (!byDomain.TryGetValue(domain, out var mod))
                throw new ArgumentException($"Backend '{domain}' not mounted.");
            return await mod.CallAsync(underlying, args).ConfigureAwait(false);
        })
        { IsDryRun = dry };
        var report = await ScriptHost.RunAsync(code, bus, plan, dry ? "dry_run" : "run", cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(report, Pretty);
    }
    case "cdp_csx_run_plan":
    {
        var code = await ResolveCsxSourceAsync(session, callArgs).ConfigureAwait(false);
        var entry = callArgs.TryGetValue("workspace_path", out var wr) && wr.GetString() is { Length: > 0 } repo
            ? repo
            : session.ProjectRoot is { Length: > 0 } pr
                ? pr
                : session.SolutionOrProjectPath is { Length: > 0 } sol
                    ? sol
                    : throw new ArgumentException(
                        "workspace_path or cdp_open session (ProjectRoot) is required for run_plan.");
        var focus = callArgs.TryGetValue("scope", out var sc) && sc.GetString() is { Length: > 0 } scopeArg
            ? scopeArg
            : session.ProjectRoot ?? session.SolutionOrProjectPath ?? entry;
        var policy = callArgs.TryGetValue("promote_policy", out var pp) && pp.GetString() is { Length: > 0 } pol
            ? pol
            : WorktreePlanRunner.PromoteOverlapSafe;
        var report = await WorktreePlanRunner.RunInWorktreeAsync(
            code,
            entry,
            async (domain, underlying, args, ct) =>
            {
                if (!byDomain.TryGetValue(domain, out var mod))
                    throw new ArgumentException($"Backend '{domain}' not mounted.");
                return await mod.CallAsync(underlying, args).ConfigureAwait(false);
            },
            cancellationToken,
            focusPath: focus,
            promotePolicy: policy).ConfigureAwait(false);
        return JsonSerializer.Serialize(report, Pretty);
    }
    case "cdp_csx_discard":
    {
        if (!callArgs.TryGetValue("plan_id", out var pid) || pid.GetString() is not { Length: > 0 } id)
            throw new ArgumentException("plan_id is required.");
        return JsonSerializer.Serialize(WorktreePlanRunner.Discard(id), Pretty);
    }
    case "cdp_csx_promote":
    {
        if (!callArgs.TryGetValue("plan_id", out var pid2) || pid2.GetString() is not { Length: > 0 } id2)
            throw new ArgumentException("plan_id is required.");
        string? policyOverride = null;
        if (callArgs.TryGetValue("promote_policy", out var ppo) && ppo.GetString() is { Length: > 0 } po)
            policyOverride = po;
        return JsonSerializer.Serialize(WorktreePlanRunner.Promote(id2, policyOverride), Pretty);
    }
    case "cdp_shell_scene":
        return shellHabitat.Scene();
    case "cdp_shell_run":
    {
        string? cmd = callArgs.TryGetValue("command", out var cmdEl) ? cmdEl.GetString() : null;
        string[]? argv = null;
        if (callArgs.TryGetValue("argv", out var argvEl) && argvEl.ValueKind == JsonValueKind.Array)
        {
            argv = argvEl.EnumerateArray()
                .Select(e => e.GetString() ?? throw new ArgumentException("argv entries must be strings."))
                .ToArray();
            if (argv.Length == 0)
                argv = null;
        }

        if (argv is null && string.IsNullOrWhiteSpace(cmd))
            throw new ArgumentException("command or argv is required.");
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        // Cursor Shell habit: working_directory — accept as cwd alias (agent-pain sticky miss).
        string? cwd = callArgs.TryGetValue("cwd", out var cwdEl) ? cwdEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(cwd)
            && callArgs.TryGetValue("working_directory", out var wdEl))
            cwd = wdEl.GetString();
        string? shell = callArgs.TryGetValue("shell", out var shEl) ? shEl.GetString() : null;
        int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
            ? to
            : IdeSettingsHabitat.EffectiveShellTimeout();
        var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
        int? codepage = callArgs.TryGetValue("codepage", out var cpEl) && cpEl.TryGetInt32(out var cp)
            ? cp
            : IdeSettingsHabitat.EffectiveShellCodepage();
        return AttachShellEvidence(Pretty, 
            shellHabitat.Run(ShellDefaults(session), cmd, tab, cwd, shell, timeout, background, codepage, argv),
            session);
    }
    case "cdp_shell_history":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        var n = callArgs.TryGetValue("n", out var nEl) && nEl.TryGetInt32(out var nn) ? nn : 20;
        return shellHabitat.History(tab, n);
    }
    case "cdp_shell_rerun":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        int? index = callArgs.TryGetValue("index", out var ixEl) && ixEl.TryGetInt32(out var ix)
            ? ix
            : null;
        int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
            ? to
            : IdeSettingsHabitat.EffectiveShellTimeout();
        var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
        return AttachShellEvidence(Pretty, 
            shellHabitat.Rerun(ShellDefaults(session), tab, index, timeout, background),
            session);
    }
    case "cdp_shell_last":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        var maxChars = callArgs.TryGetValue("max_chars", out var mcEl) && mcEl.TryGetInt32(out var mc)
            ? mc
            : 0;
        return AttachShellEvidence(Pretty, shellHabitat.Last(tab, maxChars), session);
    }
    case "cdp_shell_which":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        return shellHabitat.Which(tab);
    }
    case "cdp_shell_kill":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        return shellHabitat.Kill(tab);
    }
    case "cdp_shell_close":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        return shellHabitat.Close(tab);
    }
    default:
        return null;
        }
    }
}
