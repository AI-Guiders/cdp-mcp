using System.Net;
using System.Text;

namespace CdpMcp;

/// <summary>
/// Gatekeeper tower (ADR-0209): the eternal owner of the client-facing port.
/// Stateless HTTP proxy 8771 → target slot. Never deploys, never restarts, knows
/// nothing about deploys. Death is indistinguishable from a blink — any witness may
/// respawn it; re-bind + go.
/// </summary>
internal static class CdpGatekeeperHost
{
    public static async Task<int> RunAsync()
    {
        var listenPort = ResolvePort("CDP_GATEKEEPER_LISTEN", 8771);
        var targetPort = ResolvePort("CDP_GATEKEEPER_TARGET", 8772);

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{listenPort}/");
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Gatekeeper: cannot bind {listenPort}: {ex.Message}");
            return 2;
        }

        Console.Out.WriteLine($"Gatekeeper: http://127.0.0.1:{listenPort}/ -> 127.0.0.1:{targetPort}");
        using var forwarder = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var targetBase = $"http://127.0.0.1:{targetPort}";

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

            _ = Task.Run(() => ForwardAsync(ctx, forwarder, targetBase));
        }

        return 0;
    }

    static async Task ForwardAsync(HttpListenerContext ctx, HttpClient http, string targetBase)
    {
        try
        {
            var request = ctx.Request;
            var relative = request.Url!.PathAndQuery;
            using var outRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), targetBase + relative);

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
            try
            {
                ctx.Response.StatusCode = 502;
                var message = Encoding.UTF8.GetBytes($"Gatekeeper: {ex.Message}");
                ctx.Response.ContentLength64 = message.Length;
                ctx.Response.OutputStream.Write(message);
                ctx.Response.Close();
            }
            catch
            {
                /* client gone — best effort */
            }
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
        || header.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    static int ResolvePort(string env, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(env), out var p) && p is > 0 and < 65536 ? p : fallback;
}
