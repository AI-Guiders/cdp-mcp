using System.Collections.Frozen;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Top-level dispatch helpers peeled from Program.cs (≤ADX soft-warn peel).</summary>
internal static partial class ProgramHost
{
    internal static async Task<string> DispatchAsync(
        ProgramHostDeps deps,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken) =>
        await IdeToolDispatch.DispatchAsync(
            new IdeToolDispatchDeps
            {
                Session = deps.Session,
                DocStore = deps.DocStore,
                ByDomain = deps.ByDomain,
                Settings = deps.Settings,
                ShellHabitat = deps.ShellHabitat,
                NotifyListChanged = () => deps.NotifyListChanged(),
                DispatchMetaAsync = (n, a, ct, warm) => DispatchMetaAsync(deps, n, a, ct, warm)
            },
            name,
            callArgs,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<string> DispatchMetaAsync(
        ProgramHostDeps deps,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null) =>
        await MetaDispatch.DispatchAsync(
            new MetaDispatchDeps
            {
                Session = deps.Session,
                DocStore = deps.DocStore,
                ByDomain = deps.ByDomain,
                Modules = deps.Modules,
                AllAffordances = deps.AllAffordances,
                Settings = deps.Settings,
                McpVersion = deps.McpVersion,
                SoftOrganMetaNames = deps.SoftOrganMetaNames,
                Pretty = deps.Pretty,
                ShellHabitat = deps.ShellHabitat,
                McpOutlet = deps.McpOutlet,
                InternetBrowser = deps.InternetBrowser,
                IdeSettings = deps.IdeSettings,
                WorkspaceStore = deps.WorkspaceStore,
                WorkspaceState = deps.WorkspaceState,
                WorkspaceDbPath = deps.WorkspaceDbPath,
                ServerRef = deps.ServerRef,
                NotifyListChanged = () => deps.NotifyListChanged(),
                EnsureOpenRecentWired = deps.EnsureOpenRecentWired,
                EnsureWorkspaceDb = deps.EnsureWorkspaceDb,
                BuildVisibleTools = deps.BuildVisibleTools,
                BuildMetaTools = deps.BuildMetaTools,
                DispatchToolAsync = (n, a, ct) => DispatchAsync(deps, n, a, ct),
                DispatchCdpWork = a => DispatchCdpWork(deps, a)
            },
            name,
            callArgs,
            cancellationToken,
            warm).ConfigureAwait(false);

    internal static ShellCwdDefaults ShellDefaults(SessionContext s) =>
        new() { ProjectRoot = s.ProjectRoot, ScmRoot = s.ScmRoot };

    internal static object DispatchCdpWork(ProgramHostDeps deps, IReadOnlyDictionary<string, JsonElement> callArgs) =>
        CdpWorkDispatch.Dispatch(
            new CdpWorkDispatchDeps
            {
                Session = deps.Session,
                WorkspaceState = deps.WorkspaceState,
                RequireWorkspace = deps.RequireWorkspace,
                RequireJobRunner = deps.RequireJobRunner,
                NotifyListChanged = () => deps.NotifyListChanged()
            },
            callArgs);
}

internal sealed class ProgramHostDeps
{
    public required SessionContext Session { get; init; }
    public required DocumentBufferStore DocStore { get; init; }
    public required IReadOnlyDictionary<string, ICdpBackendModule> ByDomain { get; init; }
    public required List<ICdpBackendModule> Modules { get; init; }
    public required ToolAffordance[] AllAffordances { get; init; }
    public required CdpSettings Settings { get; init; }
    public required string McpVersion { get; init; }
    public required HashSet<string> SoftOrganMetaNames { get; init; }
    public required JsonSerializerOptions Pretty { get; init; }
    public required ShellHabitat ShellHabitat { get; init; }
    public required McpOutletHabitat McpOutlet { get; init; }
    public required InternetBrowserHabitat InternetBrowser { get; init; }
    public required IdeSettingsHabitat IdeSettings { get; init; }
    public required IntentWorkspaceStore WorkspaceStore { get; init; }
    public required IntentWorkspaceState WorkspaceState { get; init; }
    public required string WorkspaceDbPath { get; init; }
    public McpServer? ServerRef { get; set; }
    public required Action NotifyListChanged { get; init; }
    public required Action EnsureOpenRecentWired { get; init; }
    public required Action EnsureWorkspaceDb { get; init; }
    public required Func<List<Tool>> BuildVisibleTools { get; init; }
    public required Func<List<Tool>> BuildMetaTools { get; init; }
    public required Func<IntentWorkspaceStore> RequireWorkspace { get; init; }
    public required Func<IdeReportJobRunner> RequireJobRunner { get; init; }
}