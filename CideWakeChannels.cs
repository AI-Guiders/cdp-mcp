#nullable enable
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// ADR-0213 WakeDispatcher — тонкие каналы доставки. Канал ничего не знает про стор,
/// тормоза или статусы очереди: принял (target, message) → отдал prompt → вернул
/// {ok, mode, detail}. Выбор канала, тормоза и статусы доставки — работа диспетчера.
/// OpenCode каналы:
///   CLI  — `cmd /c opencode.cmd run -s &lt;session&gt; "message"` (PATHEXT через cmd —
///          урок b02d343: npm-шим не стартует голым Process.Start из сервисного cwd);
///   HTTP — POST {server}/session/{id}/prompt_async (опциональная Basic-auth,
///          env CDP_OPENCODE_PASSWORD/USERNAME, CDP_OPENCODE_URL/ DIRECTORY).
/// </summary>
internal static class CideWakeChannels
{
    public static bool IsOk(object result) =>
        result.GetType().GetProperty("ok")?.GetValue(result) is true;

    public static class Opencode
    {
        public static bool IsConfigured() => BinaryAvailable();

        static string Bin =>
            Environment.GetEnvironmentVariable("CDP_OPENCODE_BIN")?.Trim() is { Length: > 0 } b
                ? b
                : "opencode";

        /// <summary>CLI-доставка: detached spawn, fail fast только при мгновенной смерти процесса.</summary>
        public static async Task<object> SendCliAsync(string session, string message, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {QuoteArg(Bin)} run -s {session} {QuoteArg(message)}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is null)
                    return Err("spawn_failed", $"Could not start {Bin} via cmd /c", 0);

                await Task.Delay(2000, ct).ConfigureAwait(false);
                if (proc.HasExited && proc.ExitCode != 0)
                    return Err("nonzero_exit", $"exit={proc.ExitCode}", proc.ExitCode);

                return new
                {
                    ok = true,
                    submit_kind = "opencode",
                    channel = "opencode",
                    mode = "cli_detached",
                    session,
                    detail = "prompt delivered (detached)"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Err("exception", ex.Message, 0);
            }
        }

        /// <summary>HTTP-доставка на opencode-сервер (prompt_async). URL решает вызывающий.</summary>
        public static async Task<object> SendHttpAsync(
            string baseUrl, string session, string message, CancellationToken ct)
        {
            try
            {
                var endpoint =
                    $"{baseUrl.TrimEnd('/')}/session/{Uri.EscapeDataString(session)}/prompt_async";
                var body = JsonSerializer.Serialize(new
                {
                    parts = new[] { new { type = "text", text = message } }
                });
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                ApplyAuth(req);
                ApplyDirectory(req);
                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    return Err("http_" + (int)resp.StatusCode,
                        string.IsNullOrWhiteSpace(text) ? resp.ReasonPhrase ?? "http error" : text,
                        (int)resp.StatusCode);
                }

                return new
                {
                    ok = true,
                    submit_kind = "opencode",
                    channel = "opencode",
                    mode = "http",
                    session,
                    detail = $"session.prompt_async http {(int)resp.StatusCode}"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Err("exception", ex.Message, 0);
            }
        }

        /// <summary>URL сервера: env → probe :4096 → self-host `opencode serve`. Null = не поднялся.</summary>
        public static async Task<string?> TryEnsureServerUrlAsync(CancellationToken ct)
        {
            const int port = 4096;
            var url = $"http://127.0.0.1:{port}";
            if (await ProbeAsync(url, ct).ConfigureAwait(false))
                return url;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {QuoteArg(Bin)} serve --port {port} --hostname 127.0.0.1",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using var proc = Process.Start(psi);
                if (proc is null)
                    return null;
                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    if (await ProbeAsync(url, ct).ConfigureAwait(false))
                        return url;
                    if (proc.HasExited)
                        break;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        static async Task<bool> ProbeAsync(string baseUrl, CancellationToken ct)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/project");
                ApplyAuth(req);
                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                return (int)resp.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }

        static bool BinaryAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {QuoteArg(Bin)} --version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p!.WaitForExit(4000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static string? Env(string primary, string fallback) =>
            Environment.GetEnvironmentVariable(primary)?.Trim() is { Length: > 0 } v
                ? v
                : Environment.GetEnvironmentVariable(fallback)?.Trim() is { Length: > 0 } f
                    ? f
                    : null;

        static void ApplyAuth(HttpRequestMessage req)
        {
            var password = Env("CDP_OPENCODE_PASSWORD", "OPENCODE_SERVER_PASSWORD");
            if (string.IsNullOrWhiteSpace(password)) return;
            var username = Env("CDP_OPENCODE_USERNAME", "OPENCODE_SERVER_USERNAME") ?? "opencode";
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        static void ApplyDirectory(HttpRequestMessage req)
        {
            var directory = Environment.GetEnvironmentVariable("CDP_OPENCODE_DIRECTORY")?.Trim();
            if (string.IsNullOrWhiteSpace(directory)) return;
            req.Headers.TryAddWithoutValidation(
                "x-opencode-directory",
                Uri.EscapeDataString(directory));
        }

        static string QuoteArg(string arg) =>
            arg.Contains(' ') || arg.Contains('"') || arg.Contains('&')
                ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
                : arg;

        static object Err(string error, string detail, int exitCode) => new
        {
            ok = false,
            submit_kind = "opencode",
            channel = "opencode",
            error,
            detail,
            exit_code = exitCode
        };
    }
}