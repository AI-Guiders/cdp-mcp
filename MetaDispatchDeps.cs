#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TerminalMcp.Core;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>Explicit deps for MetaDispatch (Program TLS closures → record per call).</summary>
internal sealed class MetaDispatchDeps
{
    public required SessionContext Session { get; init; }
    public required DocumentBufferStore DocStore { get; init; }
    public required IReadOnlyDictionary<string, ICdpBackendModule> ByDomain { get; init; }
    public required IReadOnlyList<ICdpBackendModule> Modules { get; init; }
    public required ToolAffordance[] AllAffordances { get; init; }
    public required CdpSettings Settings { get; init; }
    public required string McpVersion { get; init; }
    public required HashSet<string> SoftOrganMetaNames { get; init; }
    public required JsonSerializerOptions Pretty { get; init; }
    public required ShellHabitat ShellHabitat { get; init; }
    public required McpOutletHabitat McpOutlet { get; init; }
    public required InternetBrowserHabitat InternetBrowser { get; init; }
    public required IdeSettingsHabitat IdeSettings { get; init; }
    public IntentWorkspaceStore? WorkspaceStore { get; init; }
    public required IntentWorkspaceState WorkspaceState { get; init; }
    public required string WorkspaceDbPath { get; init; }
    public McpServer? ServerRef { get; init; }
    public required Action NotifyListChanged { get; init; }
    public required Action EnsureOpenRecentWired { get; init; }
    public required Action EnsureWorkspaceDb { get; init; }
    public required Func<List<Tool>> BuildVisibleTools { get; init; }
    public required Func<List<Tool>> BuildMetaTools { get; init; }
    public required Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> DispatchToolAsync { get; init; }
    public required Func<IReadOnlyDictionary<string, JsonElement>, object> DispatchCdpWork { get; init; }
}
