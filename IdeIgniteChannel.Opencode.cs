#nullable enable
using System.Diagnostics;
using System.Text;

namespace CdpMcp;

/// <summary>
/// OpenCode fire channel for AutoIgnition — native wake into an opencode session.
/// Sibling to the Cursor/CDT provider (IdeIgniteChannel); keeps cursor-specific logic out of here.
/// Config-gated via env: CDP_OPENCODE_SESSION (session id) + CDP_OPENCODE_BIN (default "opencode").
/// fire = `opencode run -s &lt;session&gt; &lt;message&gt;` (resume same session, native, no UI inject).
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
        var session = Environment.GetEnvironmentVariable("CDP_OPENCODE_SESSION")?.Trim();
        var bin = Environment.GetEnvironmentVariable("CDP_OPENCODE_BIN")?.Trim();
        if (string.IsNullOrWhiteSpace(bin))
            bin = "opencode";

        if (string.IsNullOrWhiteSpace(session))
        {
            return ErrOpencode("opencode", "no_session",
                "CDP_OPENCODE_SESSION not set — point AutoI at an opencode session id.", 0);
        }

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
