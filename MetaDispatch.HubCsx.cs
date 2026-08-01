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
    static async Task<string?> HubCsxAsync(
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
    default:
        return null;
        }
    }
}
