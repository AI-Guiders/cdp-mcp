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
    case "cdp_peel":
        return await IdePeelChannel.HandleAsync(session, byDomain, callArgs, cancellationToken).ConfigureAwait(false);
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
    case "cdp_glass":
        return IdeGlassSurfaceChannel.HandleJson(session, callArgs);
    case "cdp_pressure":
        return IdePressureChannel.HandleJson(session, callArgs);
    case "cdp_domain":
        return IdeDomainChannel.HandleJson(session, callArgs);
    case "cdp_calendar":
        return IdeCalendarChannel.HandleJson(session, callArgs);
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
    default:
    {
        var pkg = await IdePackagesAsync(d, name, callArgs, cancellationToken).ConfigureAwait(false);
        if (pkg is not null)
            return pkg;
        return null;
    }

        }
    }
}