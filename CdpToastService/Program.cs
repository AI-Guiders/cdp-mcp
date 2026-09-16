#nullable enable
using System.Text;
using System.Text.Json;
using Windows.UI.Notifications;

namespace CdpToastService;

/// <summary>
/// CDP-ADR-0228 — операторский тост-сервис (Windows-only, отдельный процесс).
/// Потребитель того же NotificationCenter, что и линии: подписывается на
/// lifecycle-события через thin API тауэра (ADR-0227) и показывает нативные
/// Windows-тосты (WinRT, без внешних пакетов). Тауэр остаётся
/// кросс-платформенным и не знает про тосты.
///
/// Запуск: `CdpToastService.exe` (вручную, автозапуск или планировщик —
/// по решению оператора). Reconnect — как у dsh-plugin-cdp.
/// </summary>
internal static class Program
{
    const string DefaultBaseUrl = "http://127.0.0.1:8771/";
    const string OperatorNick = "оператор";
    const int ReconnectMs = 15_000;
    const int ReadTimeoutMs = 30_000;
    static readonly string[] Events = ["peer_ship", "build_finished", "test_finished"];

    static async Task<int> Main(string[] args)
    {
        var baseUrl = Arg(args, "--base-url") ?? DefaultBaseUrl;
        var tokenPath = Arg(args, "--token")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cdp-mcp", "service-token");
        var bridgeSession = Guid.NewGuid().ToString("N");

        Console.WriteLine($"CdpToastService: base={baseUrl} nick={OperatorNick} events=[{string.Join(',', Events)}]");

        var token = (await File.ReadAllTextAsync(tokenPath)).Trim();

        // Подписка (идемпотентно).
        foreach (var ev in Events)
        {
            var ok = await SubscribeAsync(baseUrl, token, bridgeSession, ev);
            Console.WriteLine($"subscribe {ev}: {(ok ? "ok" : "FAIL")}");
        }

        // SSE-вотч с reconnect.
        while (true)
        {
            try
            {
                await WatchAsync(baseUrl, token, bridgeSession);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"watch error: {ex.Message}; reconnecting in {ReconnectMs}ms");
            }
            await Task.Delay(ReconnectMs);
        }
    }

    static async Task<bool> SubscribeAsync(string baseUrl, string token, string bridgeSession, string ev)
    {
        try
        {
            using var client = Http(baseUrl, token, bridgeSession);
            using var resp = await client.PostAsync("api/v1/wake/subscribe", Json(new { nick = OperatorNick, @event = ev }));
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    static async Task WatchAsync(string baseUrl, string token, string bridgeSession)
    {
        using var client = Http(baseUrl, token, bridgeSession);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/wake/watch?nick={Uri.EscapeDataString(OperatorNick)}");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // Таймаут чтения: вышка шлёт SSE-heartbeat (: ping) каждые 15с —
        // если 30с тишины, стрим мёртв/завис — reconnect (CDP-ADR-0228).
        while (true)
        {
            using var timeout = new CancellationTokenSource(ReadTimeoutMs);
            string? line;
            try
            {
                line = await reader.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new IOException($"watch read timeout ({ReadTimeoutMs}ms no data)");
            }
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var raw = line["data:".Length..].Trim();
            if (raw.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var kind = doc.RootElement.TryGetProperty("kind", out var k) ? k.GetString() : null;
                var body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null;
                if (kind is null) continue;
                Toast($"CDP: {kind}", body ?? "—");
                Console.WriteLine($"toast: {kind} — {body}");
            }
            catch
            {
                /* malformed payload — ignore */
            }
        }
        throw new IOException("watch stream closed");
    }

    static HttpClient Http(string baseUrl, string token, string bridgeSession)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-CDP-Bridge-Session", bridgeSession);
        client.DefaultRequestHeaders.Add("X-CDP-Workspace-Key", "cdp");
        client.DefaultRequestHeaders.Add("X-CDP-Composer", "toast");
        return client;
    }

    static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    /// <summary>Нативный Windows-тост (WinRT; TFM net10.0-windows — проекции в SDK).</summary>
    static void Toast(string title, string body)
    {
        try
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(body));
            ToastNotificationManager.CreateToastNotifier("CdpToastService").Show(new ToastNotification(xml));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"toast failed: {ex.Message}");
        }
    }

    static string? Arg(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
