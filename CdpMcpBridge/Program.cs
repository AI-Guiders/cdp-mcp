using System.Collections.Frozen;
using System.Net.Http.Json;
using System.Text.Json;
using CdpMcpBridge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tomlyn;
using Tool = ModelContextProtocol.Protocol.Tool;

var load = CdpBridgeConfigLoader.Load(args);
if (load.IsHelp)
{
    PrintUsage();
    return 0;
}

if (!load.IsSuccess || load.Settings is null)
{
    Console.Error.WriteLine(load.Error ?? "Failed to load bridge config.");
    PrintUsage();
    return 1;
}

var settings = load.Settings;
var tenantState = new CdpBridgeTenantHeadersState
{
    BridgeSessionId = Guid.NewGuid().ToString("N"),
    WorkspaceKey = CdpBridgeIdentity.ResolveWorkspaceKey(load.ConfigPath),
    Composer = Environment.GetEnvironmentVariable("CDP_COMPOSER") ?? "main"
};
using var http = CdpBridgeHttpClient.Create(settings, tenantState);
var serviceEnsurer = new CdpBridgeServiceEnsurer(settings);
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var invokeRouter = new CdpBridgeInvokeRouter(settings, http, serviceEnsurer, jsonOptions);

var options = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "CdpMcpBridge", Version = "0.2.0" },
    ServerInstructions = """
        CDP MCP bridge — thin stdio transport to durable CdpService (ADR-0198).
        SSOT handlers live in CdpService; bridge forwards ListTools/CallTool.
        Watches capabilitiesRev (ADR-0202) and emits tools/list_changed when CdpService rev bumps.
        Deploy gap (ADR-0203): cdp_deploy apply|hard|rollout blocks until durable job + service health;
        cdp_lifecycle_* reads local job store when service is down; ensurer skips auto-start during deploy.
        When [service] install_dir is set, bridge auto-starts CdpService on connection refused (cold boot only).
        Otherwise run Start-CdpService.ps1 or cdp deploy hard.
        """,
    ProtocolVersion = "2024-11-05",
    Capabilities = new ServerCapabilities
    {
        Tools = new ToolsCapability { ListChanged = true }
    },
    Handlers = new McpServerHandlers
    {
        ListToolsHandler = async (_, cancellationToken) =>
        {
            try
            {
                return await CdpBridgeTransport.WithRetryAsync(
                    serviceEnsurer,
                    CdpBridgeInvokeContext.Default,
                    async ct =>
                    {
                        using var response = await http.GetAsync("/api/v1/cdp/capabilities", ct)
                            .ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        var payload = await response.Content.ReadFromJsonAsync<CdpCapabilitiesResponse>(jsonOptions, ct)
                            .ConfigureAwait(false);
                        var tools = (payload?.Tools ?? [])
                            .Select(t => new Tool
                            {
                                Name = t.Name,
                                Description = t.Description,
                                InputSchema = t.InputSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                                    ? JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
                                    : t.InputSchema
                            })
                            .ToList();
                        return new ListToolsResult { Tools = tools };
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var hint = serviceEnsurer.CanAutoStart
                    ? "Auto-start failed or service still down."
                    : "Set [service] install_dir or run Start-CdpService.ps1.";
                return new ListToolsResult
                {
                    Tools =
                    [
                        new Tool
                        {
                            Name = "cdp_health",
                            Description = $"CdpService unreachable at {settings.BaseUrl}: {ex.Message}. {hint}"
                        }
                    ]
                };
            }
        },
        CallToolHandler = async (request, cancellationToken) =>
        {
            var conversationId = CdpBridgeConversationMeta.TryResolve(request.Params);
            using var conversationScope = CdpBridgeConversationContext.Bind(conversationId);

            var name = request.Params?.Name ?? "";
            var args = request.Params?.Arguments is IReadOnlyDictionary<string, JsonElement> dictionary
                ? dictionary
                : FrozenDictionary<string, JsonElement>.Empty;

            try
            {
                return await invokeRouter.InvokeAsync(name, args, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var deployHint = CdpBridgeDurableAccess.HasInFlightDeploy()
                    ? " Durable deploy in flight — bridge will resume when supervisor restarts CdpService."
                    : "";
                var hint = serviceEnsurer.CanAutoStart
                    ? "Auto-start failed or service still down." + deployHint
                    : "Set [service] install_dir or run Start-CdpService.ps1." + deployHint;
                return Error($"CdpService unreachable at {settings.BaseUrl}: {ex.Message}. {hint}");
            }
        }
    }
};

static CallToolResult Error(string message) =>
    new()
    {
        Content = [new TextContentBlock { Text = $"Error: {message}" }],
        IsError = true
    };

static void PrintUsage()
{
    Console.Error.WriteLine(
        """
        CdpMcpBridge — stdio MCP → durable CdpService HTTP (ADR-0198).

        Usage:
          CdpMcpBridge [--config|-c PATH]

        Config (TOML — same cdp-mcp.toml as service):
          [service]
          bind = "127.0.0.1"
          port = 8771
          token_path = ""   # optional; default %LocalAppData%/cdp-mcp/service-token
          install_dir = "D:/cdp-service"  # bridge auto-starts sidecar when down
          auto_start = true               # default true when install_dir set

        Env overrides:
          CDP_SERVICE_URL=http://127.0.0.1:8771
          CDP_SERVICE_TOKEN=...
        """);
}

var transport = new StdioServerTransport("CdpMcpBridge");
await using var server = McpServer.Create(transport, options);
Console.Error.WriteLine($"CdpMcpBridge → {settings.BaseUrl} bridge={tenantState.BridgeSessionId} ws={tenantState.WorkspaceKey}");

using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
var watcher = new CdpBridgeCapabilitiesWatcher(settings, tenantState, CdpBridgeCapabilitiesPoll.ResolveInterval());
var watchTask = watcher.RunAsync(server, watchCts.Token);
var rootsTask = CdpBridgeRootsSync.RunAsync(server, tenantState, watchCts.Token);

try
{
    await server.RunAsync(CancellationToken.None);
}
finally
{
    watchCts.Cancel();
    try { await watchTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
}

return 0;

internal sealed class CdpCapabilitiesResponse
{
    public CdpCapabilityTool[]? Tools { get; set; }
}

internal sealed class CdpCapabilityTool
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public JsonElement InputSchema { get; set; }
}

internal sealed class CdpInvokeRequest
{
    public string? Tool { get; set; }
    public Dictionary<string, JsonElement>? Arguments { get; set; }
}

internal sealed class CdpInvokeResponse
{
    public bool Success { get; set; }
    public string Body { get; set; } = "";
}
