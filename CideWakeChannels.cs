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
    /// <summary>Log opencode — источник истины о занятости сессии (Света 2026-09-07:
    /// письмо прервало генерацию Ток — «почта перебивает черновик»; вежливый почтальон
    /// ждёт «exiting loop»).</summary>
    internal static string? LogPathOverrideForTests { get; set; }

    static string OpencodeLogPath =>
        LogPathOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode", "log", "opencode.log");

    /// <summary>Хвост лога сессии: (timestamp, event) последней записи по session.id.</summary>
    public static (string? Ts, string? Ev) LastSessionEvent(string logTail, string session)
    {
        string? ts = null, ev = null;
        foreach (var line in logTail.Split('\n'))
        {
            if (!line.Contains(session, StringComparison.Ordinal))
                continue;
            var t = System.Text.RegularExpressions.Regex.Match(line, @"timestamp=([^\s]+)");
            ts = t.Success ? t.Groups[1].Value : ts;
            var e = System.Text.RegularExpressions.Regex.Match(line, @"message=(""?(?<ev>[^\r\n""]*)""?)");
            ev = e.Success ? e.Groups["ev"].Value : ev;
        }
        return (ts, ev);
    }

    /// <summary>Вежливый почтальон: занята ли сессия генерацией?
    /// Последняя запись сессии — stream/loop-step (без «exiting loop») → busy.
    /// Застарелый busy (старше 5 минут) — не блокирует (защита от застрявшего хода/лога).</summary>
    public static bool IsSessionBusy(string session)
    {
        try
        {
            var logPath = OpencodeLogPath;
            if (!File.Exists(logPath))
                return false;
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var tailLen = Math.Min(fs.Length, 400_000);
            fs.Seek(-tailLen, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            var (ts, ev) = LastSessionEvent(reader.ReadToEnd(), session);
            if (ev is null)
                return false;
            if (ev.Contains("exiting loop", StringComparison.Ordinal))
                return false;
            if (ts is not null
                && DateTimeOffset.TryParse(ts, out var stamp)
                && DateTimeOffset.UtcNow - stamp.ToUniversalTime() > TimeSpan.FromMinutes(5))
                return false; // застрявший busy — не блокируем вечно
            return true;
        }
        catch
        {
            return false; // ошибка чтения лога — не блокируем доставку
        }
    }

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
        // Класс-B устранение гонки run'ов (Света 2026-09-08): пустые user-ходы рождаются, когда
        // два run'а конкурируют за одну сессию. Один in-flight wake на сессию — check+send
        // атомарны внутри процесса, тики диспетча не соревнуются за одну линию.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> SessionGates =
            new(StringComparer.Ordinal);

        public static SemaphoreSlim SessionGate(string session) =>
            SessionGates.GetOrAdd(session, _ => new SemaphoreSlim(1, 1));
        /// <summary>Seam для тестов (NSubstitute): SendCli подменяется, IsSessionBusy остаётся реальным (чтение лога).</summary>
        public static IOpencodeWakeTransport Transport { get; set; } = RealOpencodeWakeTransport.Instance;



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

        /// <summary>Canonical no-session failure (dispatcher-level guard result).</summary>
        public static object ErrNoSession() =>
            new
            {
                ok = false,
                submit_kind = "opencode",
                channel = "opencode",
                error = "no_session",
                detail = "No OpenCode session — target session required.",
                exit_code = 0
            };

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