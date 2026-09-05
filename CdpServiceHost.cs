using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;

namespace CdpMcp;

internal static class CdpServiceHost
{
    internal static async Task<int> RunAsync(string configPath, string[] args, CancellationToken cancellationToken = default)
    {
        CdpServiceProcessReclaim.Ensure();
        // ADR-0212 stage (d): the postman lives in the tower — polls per-line wake
        // notes (arms/line-*.json) and delivers them to registered opencode sessions.
        new LineWakePoller().Start();
        await using var runtime = await CdpHostRuntime.CreateAsync(configPath, cancellationToken).ConfigureAwait(false);
        var settings = runtime.Settings.Service;
        var token = CdpServiceToken.Ensure(settings);
        // ADR-0209: the gatekeeper owns 8771 forever; the service self-registers on a free slot port.
        var slotPort = CdpSlotRegistry.PickFreePort();
        var baseUrl = $"http://{settings.Bind}:{slotPort}";
        var slot = new CdpSlotRecord
        {
            Pid = Environment.ProcessId,
            Port = slotPort,
            Sha = CdpHostRuntime.BuildStamp().Sha,
            BuildUtc = CdpHostRuntime.BuildStamp().Utc,
            LastSeenUtc = DateTimeOffset.UtcNow
        };
        CdpSlotRegistry.Upsert(CdpProfile.StateRoot, slot);
        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatTask = HeartbeatSlotAsync(slot, heartbeatCts.Token);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls(baseUrl);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(settings);
        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "CdpService",
                Version = runtime.McpVersion
            };
            options.ServerInstructions = ProgramHost.ServerInstructions;
            options.ProtocolVersion = "2024-11-05";
            options.Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = true }
            };
        })
            .WithHttpTransport(httpOptions =>
            {
                httpOptions.Stateless = true;
                httpOptions.ConfigureSessionOptions = (httpContext, mcpOptions, _) =>
                {
                    var rt = httpContext.RequestServices.GetRequiredService<CdpHostRuntime>();
                    mcpOptions.Handlers = new McpServerHandlers
                    {
                        ListToolsHandler = (_, ct) =>
                        {
                            ct.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(new ListToolsResult { Tools = rt.ListTools() });
                        },
                        CallToolHandler = async (request, ct) =>
                        {
                            var name = request.Params?.Name ?? "";
                            var callArgs = request.Params?.Arguments is IReadOnlyDictionary<string, JsonElement> d
                                ? d
                                : FrozenDictionary<string, JsonElement>.Empty;
                            var tenantKey = CdpTenantHeaders.TryParse(httpContext.Request.Headers);
                            var result = await rt.InvokeToolAsync(name, callArgs, ct, tenantKey).ConfigureAwait(false);
                            return rt.ToCallToolResult(result);
                        }
                    };
                    return Task.CompletedTask;
                };
            });

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            if (!CdpServiceToken.IsAuthorized(context, settings))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { detail = "Unauthorized" }).ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        app.MapGet("/healthz", (CdpHostRuntime rt) => Results.Json(new
        {
            ok = true,
            service = "CdpService",
            version = rt.McpVersion,
            build = CdpHostRuntime.BuildFingerprint,
            backends = rt.Backends.Keys.ToArray(),
            capabilitiesRev = rt.CapabilitiesRevision,
            tenants = rt.TenantCount,
            tenants_active = rt.TenantSnapshots.Select(t => new
            {
                wire = t.Wire,
                bridge = t.BridgeSession,
                workspace = t.WorkspaceKey,
                composer = t.Composer,
                last_touch_utc = t.LastTouchUtc,
                project_root = t.ProjectRoot
            }),
            multiplex = "ADR-0200"
        }));

        app.MapGet("/api/v1/cdp/tenant/composer", (HttpContext http) =>
        {
            var key = CdpTenantHeaders.TryParse(http.Request.Headers);
            if (key is null)
                return Results.Json(new { composer = "main", adr = "0200" });
            var conversationId = CdpTenantHeaders.ReadConversationId(http.Request.Headers);
            return Results.Json(CdpTenantComposerLatch.Snapshot(key.Value.BridgeSession, conversationId));
        });

        app.MapGet("/api/v1/cdp/capabilities", (CdpHostRuntime rt) => Results.Json(new
        {
            capabilitiesRev = rt.CapabilitiesRevision,
            // Bridge ListTools needs inputSchema — name/description alone yields empty schemas in Cursor.
            tools = rt.ListTools().Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = t.InputSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
                    : t.InputSchema
            }).ToArray()
        }));

        app.MapGet("/api/v1/cdp/capabilities/watch", async (HttpContext context, CdpHostRuntime rt, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/event-stream";
            await foreach (var rev in rt.WatchCapabilitiesRevisionAsync(ct).ConfigureAwait(false))
            {
                await context.Response
                    .WriteAsync($"event: rev\ndata: {{\"capabilitiesRev\":{rev}}}\n\n", ct)
                    .ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        });

        app.MapPost("/api/v1/cdp/invoke", async (
            CdpInvokeRequest request,
            CdpHostRuntime rt,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Tool))
                return Results.BadRequest(new { detail = "tool is required." });

            var args = request.Arguments is null
                ? FrozenDictionary<string, JsonElement>.Empty
                : request.Arguments.ToFrozenDictionary(StringComparer.Ordinal);

        var tenantKey = CdpTenantHeaders.TryParse(http.Request.Headers);
            var conversationId = CdpTenantHeaders.ReadConversationId(http.Request.Headers);
            using var routingScope = CdpTenantRoutingContext.Enter(conversationId);
            var result = await rt.InvokeToolAsync(request.Tool, args, ct, tenantKey).ConfigureAwait(false);
            return Results.Json(new CdpInvokeResponse
            {
                Success = !result.IsError,
                Body = result.Body
            }, statusCode: result.IsError ? StatusCodes.Status400BadRequest : StatusCodes.Status200OK);
        });

        Console.Error.WriteLine(
            $"CdpService {runtime.McpVersion} listening {baseUrl} token={settings.ResolveTokenPath()} backends=[{string.Join(",", runtime.Backends.Keys)}]");
                try
        {
            await app.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); }
            catch { /* shutdown race */ }
            CdpSlotRegistry.Remove(CdpProfile.StateRoot, slot.Pid);
        }
        return 0;
    }
    static async Task HeartbeatSlotAsync(CdpSlotRecord slot, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            slot.LastSeenUtc = DateTimeOffset.UtcNow;
            CdpSlotRegistry.Upsert(CdpProfile.StateRoot, slot);
        }
    }

}

internal sealed class CdpInvokeRequest
{
    public string? Tool { get; set; }
    public Dictionary<string, JsonElement>? Arguments { get; set; }
}

internal sealed class CdpInvokeResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
}

