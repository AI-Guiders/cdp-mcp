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
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var options = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "CdpMcpBridge", Version = "0.1.0" },
    ServerInstructions = """
        CDP MCP bridge — thin stdio transport to durable CdpService (ADR-0198).
        SSOT handlers live in CdpService; this process only forwards ListTools/CallTool.
        Watches capabilitiesRev (ADR-0202) and emits tools/list_changed when CdpService rev bumps.
        If tools fail with unreachable — run Start-CdpService.ps1 or cdp deploy hard.
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
                using var response = await http.GetAsync("/api/v1/cdp/capabilities", cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<CdpCapabilitiesResponse>(jsonOptions, cancellationToken)
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
            }
            catch (Exception ex)
            {
                return new ListToolsResult
                {
                    Tools =
                    [
                        new Tool
                        {
                            Name = "cdp_health",
                            Description = $"CdpService unreachable at {settings.BaseUrl}: {ex.Message}. Start CdpService."
                        }
                    ]
                };
            }
        },
        CallToolHandler = async (request, cancellationToken) =>
        {
            var name = request.Params?.Name ?? "";
            var args = request.Params?.Arguments is IReadOnlyDictionary<string, JsonElement> dictionary
                ? dictionary
                : FrozenDictionary<string, JsonElement>.Empty;

            try
            {
                var payload = new CdpInvokeRequest
                {
                    Tool = name,
                    Arguments = args.Count == 0
                        ? null
                        : args.ToDictionary(static p => p.Key, static p => p.Value)
                };
                using var response = await http.PostAsJsonAsync("/api/v1/cdp/invoke", payload, jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                var body = await response.Content.ReadFromJsonAsync<CdpInvokeResponse>(jsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                    ?? new CdpInvokeResponse { Success = false, Body = await response.Content.ReadAsStringAsync(cancellationToken) };
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = body.Body }],
                    IsError = !body.Success
                };
            }
            catch (Exception ex)
            {
                return Error($"CdpService unreachable at {settings.BaseUrl}: {ex.Message}");
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
