#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Backends;
using ModelContextProtocol.Protocol;
using TerminalMcp.Core;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>Shared read-mostly substrate — one instance per CdpService (ADR-0200).</summary>
internal sealed class CdpSharedKernel
{
    public required string ConfigPath { get; init; }
    public required CdpSettings Settings { get; init; }
    public required List<ICdpBackendModule> Modules { get; init; }
    public required IReadOnlyDictionary<string, ICdpBackendModule> ByDomain { get; init; }
    public required ToolAffordance[] AllAffordances { get; init; }
    public required string McpVersion { get; init; }
    public required JsonSerializerOptions Pretty { get; init; }
    public required McpOutletHabitat McpOutlet { get; init; }
    public required InternetBrowserHabitat InternetBrowser { get; init; }
    public required Dictionary<string, Tool> AnTools { get; init; }
    public required Dictionary<string, Tool> TkTools { get; init; }
    public required Dictionary<string, Tool> FindTools { get; init; }
    public required Dictionary<string, Tool> FailTools { get; init; }
    public required Dictionary<string, Tool> DbgTools { get; init; }
    public required Dictionary<string, Tool> BtTools { get; init; }
    public required Dictionary<string, Tool> RoslynTools { get; init; }
    public required Dictionary<string, Tool> GitTools { get; init; }
    public required Dictionary<string, Tool> HciTools { get; init; }
    public required Dictionary<string, Tool> AnuiTools { get; init; }
}
