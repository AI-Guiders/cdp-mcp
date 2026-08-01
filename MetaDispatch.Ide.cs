#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static async Task<string?> IdeAsync(
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
        var mcpVersion = d.McpVersion;
        var SoftOrganMetaNames = d.SoftOrganMetaNames;
        var Pretty = d.Pretty;
        var shellHabitat = d.ShellHabitat;
        var mcpOutlet = d.McpOutlet;
        var internetBrowser = d.InternetBrowser;
        var ideSettings = d.IdeSettings;
        var workspaceStore = d.WorkspaceStore;
        var workspaceState = d.WorkspaceState;
        var workspaceDbPath = d.WorkspaceDbPath;
        var serverRef = d.ServerRef;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureOpenRecentWired = d.EnsureOpenRecentWired;
        var EnsureWorkspaceDb = d.EnsureWorkspaceDb;
        var BuildVisibleTools = d.BuildVisibleTools;
        var BuildMetaTools = d.BuildMetaTools;
        var DispatchAsync = d.DispatchToolAsync;
        var DispatchCdpWork = d.DispatchCdpWork;

        switch (name)
        {
    case "cdp_sa":
        return IdeSaChannel.HandleJson(docStore, session, callArgs);
    case "cdp_refactor":
        return IdeRefactorPlanChannel.HandleJson(docStore, session, callArgs);
    case "cdp_debug_sa":
        return IdeDebugSaChannel.HandleJson(session, callArgs);
    case "cdp_test_sa":
        return IdeTestSaChannel.HandleJson(session, callArgs);
    case "cdp_build_sa":
        return IdeBuildSaChannel.HandleJson(session, callArgs);
    case "cdp_crm":
        return IdeCrmChannel.HandleJson(session, workspaceStore, workspaceState, callArgs);
    case "cdp_arch":
        return IdeArchBoardChannel.HandleJson(session, callArgs);
    case "cdp_onboard":
        return IdeOnboardChannel.HandleJson(session, callArgs);
    case "cdp_toolchain":
        return IdeToolchainChannel.HandleJson(session, callArgs);
    case "cdp_md_author":
        return IdeMdAuthorChannel.HandleJson(session, callArgs);
    case "cdp_fdr":
        return IdeFdrChannel.HandleJson(session, callArgs);
    case "cdp_teeth":
        return IdeTeethChannel.HandleJson(session, callArgs);
    case "cdp_postmortem":
        return IdePostmortemChannel.HandleJson(session, callArgs);
    case "cdp_learn":
        return IdeLearnChannel.HandleJson(session, callArgs);
    case "cdp_scope":
        return IdeScopeChannel.HandleJson(session, callArgs);
    case "cdp_files":
        return IdeFilesChannel.HandleJson(docStore, session, callArgs);
    case "cdp_ignite":
        return await IdeIgniteChannel.HandleJsonAsync(callArgs, cancellationToken);
    case "cdp_webcam":
        return IdeWebcamChannel.HandleJson(session, callArgs);
    case "cdp_pressure":
        return IdePressureChannel.HandleJson(session, callArgs);
    case "cdp_domain":
        return IdeDomainChannel.HandleJson(session, callArgs);
    case "cdp_icm":
        return await IdeIcmChannel.HandleJsonAsync(callArgs, cancellationToken);
    case "cdp_cockpit_host":
        return IdeCockpitHostChannel.HandleJson(callArgs);
    case "cdp_recent":
    {
        EnsureOpenRecentWired();
        var take = 12;
        if (callArgs.TryGetValue("take", out var takeEl) && takeEl.TryGetInt32(out var ti) && ti > 0)
            take = ti;
        var items = OpenRecentStore.List(take);
        return JsonSerializer.Serialize(new
        {
            count = items.Count,
            store = OpenRecentStore.Location,
            store_kind = "witdb",
            items = items.Select((e, i) => new
            {
                index = i,
                path = e.Path,
                root = e.Root,
                kind = e.Kind,
                language = e.Language,
                opened_utc = e.OpenedUtc
            })
        }, Pretty);
    }
    case "cdp_build":
        return await IdeSessionLifecycle.BuildAsync(
            session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
    case "cdp_test":
        return await IdeSessionLifecycle.TestAsync(
            session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
    case "cdp_test_scene":
        return await IdeSessionLifecycle.TestSceneAsync(
            session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
    case "cdp_test_plan":
        return await IdeSessionLifecycle.TestPlanAsync(
            session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
    case "cdp_run":
        return await IdeSessionLifecycle.RunAsync(session, callArgs, cancellationToken).ConfigureAwait(false);
    case "cdp_pkg_find":
    {
        var q = callArgs.TryGetValue("query", out var qEl) ? qEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(q))
            throw new ArgumentException("query is required.");
        var take = 5;
        if (callArgs.TryGetValue("take", out var tEl) && tEl.TryGetInt32(out var ti))
            take = ti;
        var (bus, plan) = PackageSession(session, callArgs);
        return (await PackageOps.FindAsync(bus, plan, q!, take, cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_pkg_list":
    {
        var (bus, plan) = PackageSession(session, callArgs);
        var path = OptionalPath(callArgs);
        return (await PackageOps.ListAsync(bus, plan, path, cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_pkg_add":
    {
        var id = callArgs.TryGetValue("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.");
        var ver = callArgs.TryGetValue("version", out var vEl) ? vEl.GetString() : null;
        var (bus, plan) = PackageSession(session, callArgs);
        return (await PackageOps.AddAsync(bus, plan, id!, ver, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_pkg_remove":
    {
        var id = callArgs.TryGetValue("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.");
        var (bus, plan) = PackageSession(session, callArgs);
        return (await PackageOps.RemoveAsync(bus, plan, id!, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_pkg_update":
    {
        var id = callArgs.TryGetValue("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.");
        var ver = callArgs.TryGetValue("version", out var vEl) ? vEl.GetString() : null;
        var (bus, plan) = PackageSession(session, callArgs);
        return (await PackageOps.UpdateAsync(bus, plan, id!, ver, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_pkg_outdated":
    {
        var (bus, plan) = PackageSession(session, callArgs);
        return (await PackageOps.OutdatedAsync(bus, plan, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_project_scene":
    {
        var (bus, plan) = PackageSession(session, callArgs);
        var root = callArgs.TryGetValue("root", out var rEl) ? rEl.GetString() : null;
        var includeInstalled = callArgs.TryGetValue("include_installed", out var ii)
            && ii.ValueKind == JsonValueKind.True;
        var maxExisting = callArgs.TryGetValue("max_existing", out var me) && me.TryGetInt32(out var mei)
            ? mei : ProjectScene.MaxExistingDefault;
        var maxInstalled = callArgs.TryGetValue("max_installed", out var mi) && mi.TryGetInt32(out var mii)
            ? mii : ProjectScene.MaxInstalledDefault;
        return (await ProjectOps.SceneAsync(bus, plan, root, includeInstalled, maxExisting, maxInstalled, cancellationToken)
            .ConfigureAwait(false)).ToJson();
    }
    case "cdp_project_create":
    {
        if (!callArgs.TryGetValue("output_dir", out var odEl) || odEl.GetString() is not { Length: > 0 } outputDir)
            throw new ArgumentException("output_dir is required.");
        var projName = callArgs.TryGetValue("name", out var nEl) ? nEl.GetString() : null;
        var template = callArgs.TryGetValue("template", out var tEl) && tEl.GetString() is { Length: > 0 } tmpl
            ? tmpl
            : "console";
        var policyRaw = callArgs.TryGetValue("tfm_policy", out var pEl) ? pEl.GetString() : null;
        var policy = TfmResolver.ParsePolicy(policyRaw);
        var tfm = callArgs.TryGetValue("tfm", out var fEl) ? fEl.GetString() : null;
        var engPolRaw = callArgs.TryGetValue("engine_policy", out var epEl) ? epEl.GetString() : null;
        var engPolicy = EngineResolver.ParsePolicy(engPolRaw);
        var engines = callArgs.TryGetValue("engines", out var eEl) ? eEl.GetString() : null;
        var force = callArgs.TryGetValue("force", out var fr) && fr.ValueKind == JsonValueKind.True;
        var doOpen = !callArgs.TryGetValue("open", out var op) || op.ValueKind != JsonValueKind.False;
        var (bus, plan) = PackageSession(session, callArgs);
        // PreferMostUsed scans session work root if set
        var step = await ProjectOps.CreateAsync(bus, plan, outputDir, projName, template, policy, tfm, engPolicy, engines, force, cancellationToken)
            .ConfigureAwait(false);
        string? openMeta = null;
        if (doOpen && step.Ok && step.Data is { } dataEl)
        {
            string? openPath = null;
            if (dataEl.TryGetProperty("project", out var proj) && proj.GetString() is { Length: > 0 } pp)
                openPath = pp;
            else if (dataEl.TryGetProperty("tsconfig", out var ts) && ts.GetString() is { Length: > 0 } tp)
                openPath = tp;
            else if (dataEl.TryGetProperty("outputDir", out var od) && od.GetString() is { Length: > 0 } odir)
                openPath = odir;
            else if (dataEl.TryGetProperty("output_dir", out var od2) && od2.GetString() is { Length: > 0 } odir2)
                openPath = odir2;
            if (openPath is not null)
            {
                EnsureOpenRecentWired();
                var open = settings.Languages.Detect(openPath);
                var park = docStore.ParkOutsideProject(open.Root);
                openMeta = IdeLanguageTools.ApplyOpen(session, open, park);
                shellHabitat.SyncSessionCwd(session.ProjectRoot);
                NotifyListChanged();
            }
        }

        return IdeLanguageTools.MergeStepOpenMeta(step.ToJson(), openMeta);
    }
    case "cdp_project_list":
    {
        var (bus, plan) = PackageSession(session, callArgs);
        var root = callArgs.TryGetValue("root", out var rEl) ? rEl.GetString() : null;
        return (await ProjectOps.ListAsync(bus, plan, root, cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_project_close":
    {
        session.ProjectRoot = null;
        session.ProjectKind = null;
        session.SolutionOrProjectPath = null;
        session.TsConfigPath = null;
        session.Language = null;
        await IdeLanguageTools.CloseProjectAsync().ConfigureAwait(false);
        RoslynMcp.ServiceLayer.MsBuildWorkspaceHost.Invalidate();
        RoslynMcp.ServiceLayer.DiagnosticsResultCache.InvalidateAll();
        NotifyListChanged();
        return JsonSerializer.Serialize(new { ok = true, kind = "projects.close", summary = "session_cleared" }, Pretty);
    }
    case "cdp_project_add_to_sln":
    case "cdp_sln_add":
    {
        if (!callArgs.TryGetValue("project", out var prEl) || prEl.GetString() is not { Length: > 0 } project)
            throw new ArgumentException("project is required.");
        var solution = callArgs.TryGetValue("solution", out var sEl) ? sEl.GetString() : null;
        var inRoot = callArgs.TryGetValue("in_root", out var ir) && ir.ValueKind == JsonValueKind.True;
        var solFolder = callArgs.TryGetValue("solution_folder", out var sfEl) ? sfEl.GetString() : null;
        var (bus, plan) = PackageSession(session, callArgs);
        return (await SolutionOps.AddProjectAsync(bus, plan, project, solution, inRoot, solFolder, cancellationToken)
            .ConfigureAwait(false)).ToJson();
    }
    case "cdp_sln_create":
    {
        if (!callArgs.TryGetValue("output_dir", out var odEl) || odEl.GetString() is not { Length: > 0 } outputDir)
            throw new ArgumentException("output_dir is required.");
        var slnName = callArgs.TryGetValue("name", out var nEl) ? nEl.GetString() : null;
        var force = callArgs.TryGetValue("force", out var fr) && fr.ValueKind == JsonValueKind.True;
        var doOpen = !callArgs.TryGetValue("open", out var op) || op.ValueKind != JsonValueKind.False;
        var (bus, plan) = PackageSession(session, callArgs);
        var step = await SolutionOps.CreateAsync(bus, plan, outputDir, slnName, force, doOpen, cancellationToken)
            .ConfigureAwait(false);
        string? openMeta = null;
        if (doOpen && step.Ok && step.Data is { } dataEl)
        {
            string? openPath = null;
            if (dataEl.TryGetProperty("solution", out var sol) && sol.GetString() is { Length: > 0 } sp)
                openPath = sp;
            else if (dataEl.TryGetProperty("output_dir", out var od) && od.GetString() is { Length: > 0 } odir)
                openPath = odir;
            else if (dataEl.TryGetProperty("outputDir", out var od2) && od2.GetString() is { Length: > 0 } odir2)
                openPath = odir2;
            if (openPath is not null)
            {
                EnsureOpenRecentWired();
                var open = settings.Languages.Detect(openPath);
                var park = docStore.ParkOutsideProject(open.Root);
                openMeta = IdeLanguageTools.ApplyOpen(session, open, park);
                shellHabitat.SyncSessionCwd(session.ProjectRoot);
                NotifyListChanged();
            }
        }

        return IdeLanguageTools.MergeStepOpenMeta(step.ToJson(), openMeta);
    }
    case "cdp_sln_list":
    {
        var (bus, plan) = PackageSession(session, callArgs);
        var root = callArgs.TryGetValue("root", out var rEl) ? rEl.GetString() : null;
        return (await SolutionOps.ListAsync(bus, plan, root, cancellationToken).ConfigureAwait(false)).ToJson();
    }
    case "cdp_sln_projects":
    {
        var (bus, plan) = PackageSession(session, callArgs);
        var solution = callArgs.TryGetValue("solution", out var sEl) ? sEl.GetString() : null;
        return (await SolutionOps.ListProjectsAsync(bus, plan, solution, cancellationToken).ConfigureAwait(false))
            .ToJson();
    }
    case "cdp_sln_remove":
    {
        if (!callArgs.TryGetValue("project", out var prEl) || prEl.GetString() is not { Length: > 0 } project)
            throw new ArgumentException("project is required.");
        var solution = callArgs.TryGetValue("solution", out var sEl) ? sEl.GetString() : null;
        var (bus, plan) = PackageSession(session, callArgs);
        return (await SolutionOps.RemoveProjectAsync(bus, plan, project, solution, cancellationToken)
            .ConfigureAwait(false)).ToJson();
    }
        default:
            return null;

        }
    }
}
