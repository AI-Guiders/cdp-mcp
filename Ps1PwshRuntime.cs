#nullable enable
using System.Diagnostics;
using System.Text;

namespace CdpMcp;

/// <summary>Shared pwsh resolve + exec for Ps1Scene and Ps1BufferDiagnostics.</summary>
internal static class Ps1PwshRuntime
{
    private static string? _cached;
    private static bool _resolved;

    public static string? Resolve()
    {
        if (_resolved) return _cached;
        foreach (var candidate in new[] { "pwsh", "pwsh.exe", "powershell", "powershell.exe" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    ArgumentList = { "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                if (!p.WaitForExit(5000))
                {
                    try { p.Kill(true); } catch { /* ignore */ }
                    continue;
                }

                if (p.ExitCode == 0)
                {
                    _cached = candidate;
                    _resolved = true;
                    return candidate;
                }
            }
            catch
            {
                // try next
            }
        }

        _resolved = true;
        _cached = null;
        return null;
    }

    public static async Task<(int Exit, string Stdout, string Stderr, int Ms)> RunAsync(
        string exe,
        IReadOnlyList<string> argv,
        string cwd,
        int timeoutSec,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in argv)
            psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!proc.Start())
                return (-1, "", $"failed to start {exe}", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message, (int)sw.ElapsedMilliseconds);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
        });

        var finished = await Task.Run(() => proc.WaitForExit(timeoutSec * 1000), ct).ConfigureAwait(false);
        if (!finished)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return (-1, stdout.ToString(), $"timed out after {timeoutSec}s", (int)sw.ElapsedMilliseconds);
        }

        return (proc.ExitCode, stdout.ToString(), stderr.ToString(), (int)sw.ElapsedMilliseconds);
    }
}
