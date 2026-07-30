#nullable enable
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    sealed class CdtSession : IAsyncDisposable
    {
        readonly ClientWebSocket _ws;
        int _id;

        CdtSession(ClientWebSocket ws, string pageTitle)
        {
            _ws = ws;
            PageTitle = pageTitle;
        }

        public string PageTitle { get; }

        public static async Task<CdtSession> ConnectPageAsync(int port, CancellationToken ct)
        {
            var list = await GetJsonAsync(port, "/json/list", ct).ConfigureAwait(false);
            var ranked = RankPageTargets(list);
            if (ranked.Count == 0)
                throw new InvalidOperationException("no_page_target");

            var tried = new List<string>();
            Exception? last = null;
            foreach (var target in ranked)
            {
                tried.Add($"{target.Title} (score={target.Score})");
                CdtSession? session = null;
                try
                {
                    session = await OpenWsAsync(target.Title, target.WsUrl, ct).ConfigureAwait(false);
                    // Agent shell may still be mounting TipTap — brief settle before giving up.
                    for (var i = 0; i < 8; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var st = await session.EvalStateAsync(ct).ConfigureAwait(false);
                        if (st.ComposerScoped)
                            return session;

                        await Task.Delay(150, ct).ConfigureAwait(false);
                    }

                    await session.DisposeAsync().ConfigureAwait(false);
                    session = null;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (session is not null)
                    {
                        try { await session.DisposeAsync().ConfigureAwait(false); }
                        catch { /* ignore */ }
                    }
                }
            }

            var detail = string.Join(" | ", tried);
            throw new InvalidOperationException(
                "no_agent_composer: no CDT page with ui-prompt-input (ComposerScoped). Tried: " + detail
                + (last is null ? "" : "; last=" + last.Message));
        }

        static async Task<CdtSession> OpenWsAsync(string title, string wsUrl, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
            var session = new CdtSession(ws, title);
            await session.CallAsync("Runtime.enable", null, ct).ConfigureAwait(false);
            return session;
        }

        public Task<ComposerState> EvalStateAsync(CancellationToken ct) =>
            EvalAsync<ComposerState>(StateJs, ct);

        public async Task<T> EvalAsync<T>(string expression, CancellationToken ct)
        {
            var result = await CallAsync("Runtime.evaluate", new
            {
                expression,
                returnByValue = true,
                awaitPromise = true
            }, ct).ConfigureAwait(false);

            if (result.ValueKind != JsonValueKind.Undefined &&
                result.TryGetProperty("exceptionDetails", out var ex))
                throw new InvalidOperationException("evaluate_exception: " + ex);

            if (!result.TryGetProperty("result", out var r) || !r.TryGetProperty("value", out var v))
                throw new InvalidOperationException("evaluate_no_value");

            return v.Deserialize<T>(Compact)
                ?? throw new InvalidOperationException("evaluate_deserialize");
        }

        async Task<JsonElement> CallAsync(string method, object? @params, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _id);
            object payload = @params is null
                ? new { id, method }
                : new { id, method, @params };
            var json = JsonSerializer.Serialize(payload, Compact);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

            while (true)
            {
                using var ms = new MemoryStream();
                var buffer = new byte[1024 * 64];
                WebSocketReceiveResult recv;
                do
                {
                    recv = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    ms.Write(buffer, 0, recv.Count);
                } while (!recv.EndOfMessage);

                using var doc = JsonDocument.Parse(ms.ToArray());
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var rid) && rid.TryGetInt32(out var got) && got == id)
                {
                    if (root.TryGetProperty("error", out var err))
                        throw new InvalidOperationException(err.ToString());
                    return root.TryGetProperty("result", out var result)
                        ? result.Clone()
                        : default;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }

            _ws.Dispose();
        }
    }
}
