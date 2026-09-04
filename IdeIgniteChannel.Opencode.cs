#nullable enable
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// OpenCode fire channel for AutoIgnition — native wake into an opencode session.
/// Sibling to the Cursor/CDT provider (IdeIgniteChannel); keeps cursor-specific logic out of here.
/// Two delivery modes (server API preferred, CLI fallback):
///   HTTP  — POST {CDP_OPENCODE_URL}/session/{id}/prompt_async {parts=[{type:text,text:msg}]}  (session.prompt_async)
///   CLI   — `opencode run --attach &lt;url&gt; -s &lt;session&gt; &lt;message&gt;`
/// Delivery target: arm.OpencodeSession (agent stamps session= at arm — ADR-0205). Env CDP_OPENCODE_SESSION only when session arg omitted.
/// Auth: CDP_OPENCODE_PASSWORD / CDP_OPENCODE_USERNAME (fallback OPENCODE_SERVER_*).
/// Directory: CDP_OPENCODE_DIRECTORY (x-opencode-directory header — required for desktop sidecar sessions).
/// </summary>
internal static partial class IdeIgniteChannel
{
    static string? _ensuredOpencodeUrl;
    static bool _binaryChecked;

    public static bool IsOpencodeConfigured() => OpencodeBinaryAvailable();

    static string OpencodeBinary() => "opencode";

    static bool OpencodeBinaryAvailable()
    {
        if (_binaryChecked) return true;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = OpencodeBinary(),
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            p!.WaitForExit(4000);
            _binaryChecked = p.ExitCode == 0;
        }
        catch
        {
            _binaryChecked = false;
        }

        return _binaryChecked;
    }

    static async Task<bool> ProbeAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/project");
            ApplyOpencodeHttpAuth(req);
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            return (int)resp.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resolve the opencode server URL: env wins; else probe default port; else self-host `opencode serve`.</summary>
    static async Task<string> EnsureServerUrlAsync(CancellationToken ct)
    {
        if (_ensuredOpencodeUrl is { Length: > 0 } cached) return cached;

        const int port = 4096;
        var url = $"http://127.0.0.1:{port}";

        if (await ProbeAsync(url, ct).ConfigureAwait(false))
        {
            _ensuredOpencodeUrl = url;
            return url;
        }

        var psi = new ProcessStartInfo
        {
            // cmd resolves PATH+PATHEXT for the npm shim (Process.Start won't from a service cwd).
            FileName = "cmd.exe",
            Arguments = $"/c opencode serve --port {port} --hostname 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("opencode serve failed to start");

        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (await ProbeAsync(url, ct).ConfigureAwait(false))
            {
                _ensuredOpencodeUrl = url;
                return url;
            }

            if (proc.HasExited) break;
        }

        throw new InvalidOperationException($"opencode serve did not become healthy on {url}");
    }

    public static async Task<object> FireToOpencodeAsync(
        string message,
        CancellationToken ct,
        string? session = null)
    {
        // Per-arm wake target wins — the arm is the SSOT for the target session.
        if (string.IsNullOrWhiteSpace(session))
        {
            return ErrOpencode("opencode", "no_session",
                "No OpenCode session — arm session=... required.", 0);
        }

        // Server API first — self-hosted `opencode serve` when no server is up (zero-setup wake).
        var url = await EnsureServerUrlAsync(ct).ConfigureAwait(false);
        return await FireToOpencodeHttpAsync(url, session, message, ct).ConfigureAwait(false);
    }

    static string? OpencodeEnv(string primary, string fallback) =>
        Environment.GetEnvironmentVariable(primary)?.Trim() is { Length: > 0 } v
            ? v
            : Environment.GetEnvironmentVariable(fallback)?.Trim() is { Length: > 0 } f
                ? f
                : null;

    static void ApplyOpencodeHttpAuth(HttpRequestMessage req)
    {
        var password = OpencodeEnv("CDP_OPENCODE_PASSWORD", "OPENCODE_SERVER_PASSWORD");
        if (string.IsNullOrWhiteSpace(password)) return;

        var username = OpencodeEnv("CDP_OPENCODE_USERNAME", "OPENCODE_SERVER_USERNAME") ?? "opencode";
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    static void ApplyOpencodeDirectoryHeader(HttpRequestMessage req)
    {
        var directory = Environment.GetEnvironmentVariable("CDP_OPENCODE_DIRECTORY")?.Trim();
        if (string.IsNullOrWhiteSpace(directory)) return;
        req.Headers.TryAddWithoutValidation(
            "x-opencode-directory",
            Uri.EscapeDataString(directory));
    }

    static async Task<object> FireToOpencodeHttpAsync(
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
            ApplyOpencodeHttpAuth(req);
            ApplyOpencodeDirectoryHeader(req);
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return ErrOpencode("opencode", "http_" + (int)resp.StatusCode,
                    string.IsNullOrWhiteSpace(text) ? resp.ReasonPhrase ?? "http error" : text, (int)resp.StatusCode);
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
            return ErrOpencode("opencode", "exception", ex.Message, 0);
        }
    }

    static async Task<object> FireToOpencodeCliAsync(
        string session, string message, CancellationToken ct)
    {
        var bin = Environment.GetEnvironmentVariable("CDP_OPENCODE_BIN")?.Trim();
        if (string.IsNullOrWhiteSpace(bin))
            bin = "opencode";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = bin,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var attach = Environment.GetEnvironmentVariable("CDP_OPENCODE_URL")?.Trim();
            if (!string.IsNullOrWhiteSpace(attach))
            {
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add("--attach");
                psi.ArgumentList.Add(attach);
                var directory = Environment.GetEnvironmentVariable("CDP_OPENCODE_DIRECTORY")?.Trim();
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    psi.ArgumentList.Add("--dir");
                    psi.ArgumentList.Add(directory);
                }

                var password = OpencodeEnv("CDP_OPENCODE_PASSWORD", "OPENCODE_SERVER_PASSWORD");
                if (!string.IsNullOrWhiteSpace(password))
                {
                    psi.ArgumentList.Add("--password");
                    psi.ArgumentList.Add(password);
                    var username = OpencodeEnv("CDP_OPENCODE_USERNAME", "OPENCODE_SERVER_USERNAME");
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        psi.ArgumentList.Add("--username");
                        psi.ArgumentList.Add(username);
                    }
                }
            }
            else
            {
                psi.ArgumentList.Add("run");
            }

            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(session);
            psi.ArgumentList.Add(message);

            using var proc = Process.Start(psi);
            if (proc is null)
                return ErrOpencode("opencode", "spawn_failed", $"Could not start {bin}", 0);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            var detail = (stderr ?? "").Trim();
            if (proc.ExitCode != 0)
            {
                return ErrOpencode("opencode", "nonzero_exit",
                    string.IsNullOrEmpty(detail) ? $"exit={proc.ExitCode}" : detail, proc.ExitCode);
            }

            return new
            {
                ok = true,
                submit_kind = "opencode",
                channel = "opencode",
                mode = "cli",
                session,
                detail = (stdout ?? "").Trim()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ErrOpencode("opencode", "exception", ex.Message, 0);
        }
    }

    static object ErrOpencode(string channel, string error, string detail, int exitCode) => new
    {
        ok = false,
        submit_kind = channel,
        channel,
        error,
        detail,
        exit_code = exitCode
    };
}
