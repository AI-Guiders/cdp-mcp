using System.Net.Http.Headers;
using Cdp.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace CdpMcp;

/// <summary>
/// Gatekeeper tower (ADR-0209): the eternal owner of the client-facing port (8771).
/// Stateless HTTP proxy — resolves the freshest healthy slot from the witdb registry
/// (silence &gt; 15s = suspected dead; healthz probe = final arbiter) and forwards.
/// Kestrel direct bind (not Http.sys) — port dies with the process; no orphaned HTTP.sys queue.
/// </summary>
internal static class CdpGatekeeperHost
{
    public const int DefaultListenPort = CdpTowerRoleConfig.DefaultListenPort;

    static readonly TimeSpan TargetCache = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    static readonly object TargetGate = new();
    static Uri? _target;
    static DateTimeOffset _targetAt;

    public static async Task<int> RunAsync(string? configPath = null)
    {
        var listenPort = CdpConfigLoader.MapTower(CdpConfigLoader.Load(configPath)).ListenPort;
        var baseUrl = $"http://127.0.0.1:{listenPort}";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls(baseUrl);

        using var forwarder = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var prober = new HttpClient { Timeout = ProbeTimeout };

        var app = builder.Build();

        app.MapFallback(async (HttpContext context) =>
        {
            var target = await ResolveTargetAsync(prober).ConfigureAwait(false);
            if (target is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response
                    .WriteAsync("Gatekeeper: no healthy slot in the registry.")
                    .ConfigureAwait(false);
                return;
            }

            try
            {
                await ForwardAsync(context, target, forwarder).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (TargetGate)
                {
                    _target = null;
                    _targetAt = DateTimeOffset.UtcNow;
                }

                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsync($"Gatekeeper: {ex.Message}").ConfigureAwait(false);
            }
        });

        Console.Out.WriteLine(
            $"Gatekeeper: {baseUrl}/ (Kestrel) -> slots {CdpSlotRegistry.DbPath(CdpProfile.StateRoot)}");
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    static async Task ForwardAsync(HttpContext context, Uri target, HttpClient http)
    {
        var request = context.Request;
        var relative = (request.Path + request.QueryString).ToString().TrimStart('/');
        using var outRequest = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(target, relative));

        foreach (var header in request.Headers)
        {
            if (IsRestricted(header.Key))
                continue;

            if (!outRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                outRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (request.ContentLength is > 0
            || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            outRequest.Content = new StreamContent(request.Body);
            if (request.ContentType is { } contentType)
                outRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        using var response = await http.SendAsync(outRequest, context.RequestAborted).ConfigureAwait(false);

        context.Response.StatusCode = (int)response.StatusCode;
        CopyHeaders(response.Headers, context.Response.Headers);
        CopyHeaders(response.Content.Headers, context.Response.Headers);

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (response.Headers.TransferEncodingChunked == true
            || mediaType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(context.RequestAborted).ConfigureAwait(false);
        context.Response.ContentLength = bytes.Length;
        if (bytes.Length > 0)
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Freshest healthy slot (LastSeenUtc desc, healthz probe). Cached briefly — deploys churn the registry.</summary>
    static async Task<Uri?> ResolveTargetAsync(HttpClient prober)
    {
        lock (TargetGate)
        {
            if (_target is not null && DateTimeOffset.UtcNow - _targetAt < TargetCache)
                return _target;
        }

        var rows = CdpSlotRegistry.Fresh(CdpProfile.StateRoot);
        foreach (var row in rows)
        {
            var candidate = new Uri($"http://127.0.0.1:{row.Port}/");
            try
            {
                using var probe = await prober.GetAsync(new Uri(candidate, "healthz")).ConfigureAwait(false);
                if (!probe.IsSuccessStatusCode)
                    continue;
            }
            catch
            {
                continue;
            }

            lock (TargetGate)
            {
                _target = candidate;
                _targetAt = DateTimeOffset.UtcNow;
            }

            return candidate;
        }

        lock (TargetGate)
        {
            _target = null;
            _targetAt = DateTimeOffset.UtcNow;
        }

        return null;
    }

    static void CopyHeaders(HttpHeaders source, IHeaderDictionary target)
    {
        foreach (var header in source)
        {
            if (IsRestricted(header.Key))
                continue;

            target[header.Key] = header.Value.ToArray();
        }
    }

    static bool IsRestricted(string header) =>
        header.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
        || header.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);
}
