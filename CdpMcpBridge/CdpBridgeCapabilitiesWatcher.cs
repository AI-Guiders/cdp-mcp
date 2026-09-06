using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CdpMcpBridge;

/// <summary>Polls/SSE-watches CdpService capabilitiesRev → MCP tools/list_changed (ADR-0202).</summary>
internal sealed class CdpBridgeCapabilitiesWatcher
{
    readonly HttpClient _authHttp;
    readonly HttpClient _healthHttp;
    readonly TimeSpan _pollInterval;
    long _lastRev;

    internal CdpBridgeCapabilitiesWatcher(
        CdpBridgeSettings settings,
        CdpBridgeTenantHeadersState tenantState,
        TimeSpan pollInterval)
    {
        _authHttp = CdpBridgeHttpClient.Create(settings, tenantState);
        _healthHttp = new HttpClient { BaseAddress = settings.BaseUrl };
        _pollInterval = pollInterval;
    }

    internal async Task RunAsync(McpServer server, CancellationToken cancellationToken)
    {
        try
        {
            _lastRev = await ReadHealthRevAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // Service down at start — keep the watcher alive. The first successful
            // poll/SSE read emits one list_changed (rev != 0) once the service
            // recovers, healing a degraded catalog.
            _lastRev = 0;
        }

        var sseTask = WatchSseAsync(server, cancellationToken);
        var pollTask = PollHealthAsync(server, cancellationToken);
        await Task.WhenAll(sseTask, pollTask).ConfigureAwait(false);
    }

    async Task WatchSseAsync(McpServer server, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cdp/capabilities/watch");
                using var response = await _authHttp
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var json = line["data:".Length..].Trim();
                    if (TryParseRev(json, out var rev))
                        await NotifyIfChangedAsync(server, rev, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    async Task PollHealthAsync(McpServer server, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                var rev = await ReadHealthRevAsync(cancellationToken).ConfigureAwait(false);
                await NotifyIfChangedAsync(server, rev, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
            }
        }
    }

    async Task<long> ReadHealthRevAsync(CancellationToken cancellationToken)
    {
        using var response = await _healthHttp.GetAsync("/healthz", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content
            .ReadFromJsonAsync<CdpHealthResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return payload?.CapabilitiesRev ?? 0;
    }

    async Task NotifyIfChangedAsync(McpServer server, long rev, CancellationToken cancellationToken)
    {
        if (rev == 0) return;
        lock (_gate)
        {
            if (rev == _lastRev) return;
            _lastRev = rev;
        }

        await server
            .SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        Console.Error.WriteLine($"CdpMcpBridge list_changed (capabilitiesRev={rev})");
    }

    readonly object _gate = new();

    static bool TryParseRev(string json, out long rev)
    {
        rev = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("capabilitiesRev", out var el) && el.TryGetInt64(out rev))
                return true;
        }
        catch
        {
        }

        return false;
    }

    internal sealed class CdpHealthResponse
    {
        public long CapabilitiesRev { get; set; }
    }
}

internal static class CdpBridgeCapabilitiesPoll
{
    internal static TimeSpan ResolveInterval()
    {
        var raw = Environment.GetEnvironmentVariable("CDP_BRIDGE_CAPABILITIES_POLL_MS");
        if (int.TryParse(raw, out var ms) && ms is >= 500 and <= 60_000)
            return TimeSpan.FromMilliseconds(ms);
        return TimeSpan.FromSeconds(2);
    }
}
