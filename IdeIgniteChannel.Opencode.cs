#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// OpenCode fire channel for AutoIgnition — native wake into an opencode session.
/// Sibling to the Cursor/CDT provider (IdeIgniteChannel); keeps cursor-specific logic out of here.
/// Two delivery modes (server API preferred, CLI fallback):
///   HTTP  — POST {CDP_OPENCODE_URL}/session/{id}/prompt {prompt=message}  (matches session.prompt handler)
///   CLI   — `opencode run -s &lt;session&gt; &lt;message&gt;`
/// Config-gated: CDP_OPENCODE_SESSION (+ optional CDP_OPENCODE_URL / CDP_OPENCODE_BIN).
/// </summary>
internal static partial class IdeIgniteChannel
{
    public static bool IsOpencodeConfigured()
        => !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("CDP_OPENCODE_SESSION"));

    public static async Task<object> FireToOpencodeAsync(
        string message,
        CancellationToken ct)
    {
        var url = Environment.GetEnvironmentVariable("CDP_OPENCODE_URL")?.Trim();
        var session = Environment.GetEnvironmentVariable("CDP_OPENCODE_SESSION")?.Trim();

        if (string.IsNullOrWhiteSpace(session))
        {
            return ErrOpencode("opencode", "no_session",
                "CDP_OPENCODE_SESSION not set — point AutoI at an opencode session id.", 0);
        }

        // Prefer the server API (session.prompt) over the CLI — no config-parse dependency.
        if (!string.IsNullOrWhiteSpace(url))
            return await FireToOpencodeHttpAsync(url, session, message, ct).ConfigureAwait(false);

        return await FireToOpencodeCliAsync(session, message, ct).ConfigureAwait(false);
    }

    static async Task<object> FireToOpencodeHttpAsync(
        string baseUrl, string session, string message, CancellationToken ct)
    {
        try
        {
            var endpoint = $"{baseUrl.TrimEnd('/')}/session/{Uri.EscapeDataString(session)}/prompt";
            var body = JsonSerializer.Serialize(new { prompt = message });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
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
                detail = $"session.prompt http {(int)resp.StatusCode}"
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
            psi.ArgumentList.Add("run");
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
