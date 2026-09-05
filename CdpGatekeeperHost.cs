using System.Net;
using System.Text;

namespace CdpMcp;

/// <summary>
/// Gatekeeper tower (ADR-0209): the eternal owner of the client-facing port (8771).
/// Stateless HTTP proxy — resolves the freshest healthy slot from the witdb registry
/// (silence &gt; 15s = suspected dead; healthz probe = final arbiter) and forwards.
/// Never deploys, never restarts, knows nothing about ports or toml. Death is
/// indistinguishable from a blink — any witness may respawn it; re-bind + go.
/// </summary>
internal static class CdpGatekeeperHost
{
    public const int ListenPort = 8771;

    static readonly TimeSpan TargetCache = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    static readonly object TargetGate = new();
    static Uri? _target;
    static DateTimeOffset _targetAt;

    public static async Task<int> RunAsync()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{ListenPort}/");
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Gatekeeper: cannot bind {ListenPort}: {ex.Message}");
            return 2;
        }

        Console.Out.WriteLine($"Gatekeeper: http://127.0.0.1:{ListenPort}/ -> slots {CdpSlotRegistry.DbPath(CdpProfile.StateRoot)}");
        using var forwarder = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var prober = new HttpClient { Timeout = ProbeTimeout };

        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (!listener.IsListening)
            {
                break;
            }
            catch (Exception)
            {
                continue;
            }

            _ = Task.Run(() => ForwardAsync(ctx, forwarder, prober));
        }

        return 0;
    }

    static async Task ForwardAsync(HttpListenerContext ctx, HttpClient http, HttpClient prober)
    {
        var target = await ResolveTargetAsync(prober).ConfigureAwait(false);
        if (target is null)
        {
            Fail(ctx, 503, "Gatekeeper: no healthy slot in the registry.");
            return;
        }

        try
        {
            var request = ctx.Request;
            var relative = request.Url!.PathAndQuery.TrimStart('/');
            using var outRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), new Uri(target, relative));

            foreach (var headerKey in request.Headers.AllKeys)
                if (!IsRestricted(headerKey))
                    outRequest.Headers.TryAddWithoutValidation(headerKey, request.Headers[headerKey]);

            if (request.HasEntityBody)
            {
                var body = await ReadBodyAsync(request).ConfigureAwait(false);
                outRequest.Content = new ByteArrayContent(body);
                if (request.Headers["Content-Type"] is { } contentType)
                    outRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            using var response = await http.SendAsync(outRequest).ConfigureAwait(false);

            ctx.Response.StatusCode = (int)response.StatusCode;
            CopyHeaders(response.Headers, ctx.Response.Headers);
            CopyHeaders(response.Content.Headers, ctx.Response.Headers);

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            ctx.Response.ContentLength64 = bytes.Length;
            if (bytes.Length > 0)
                await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            // Target died between resolve and forward — drop the cache so the next request re-probes.
            lock (TargetGate)
            {
                _target = null;
                _targetAt = DateTimeOffset.UtcNow;
            }

            Fail(ctx, 502, $"Gatekeeper: {ex.Message}");
        }
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

    static void Fail(HttpListenerContext ctx, int status, string message)
    {
        try
        {
            ctx.Response.StatusCode = status;
            var bytes = Encoding.UTF8.GetBytes(message);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        }
        catch
        {
            /* client gone — best effort */
        }
    }

    static void CopyHeaders(System.Net.Http.Headers.HttpHeaders source, WebHeaderCollection target)
    {
        foreach (var header in source)
            if (!IsRestricted(header.Key))
                target[header.Key] = string.Join(", ", header.Value);
    }

    static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request)
    {
        using var stream = request.InputStream;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(false);
        return memory.ToArray();
    }

    static bool IsRestricted(string header) =>
        header.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
        || header.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
        || header.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);
}
