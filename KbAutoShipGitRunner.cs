using System.Diagnostics;

namespace CdpMcp;

internal readonly record struct KbGitExecResult(int ExitCode, string Stdout, string Stderr);

/// <summary>Thin git exec for KB AutoShip — mockable in tests.</summary>
internal sealed class KbAutoShipGitRunner
{
    internal static Func<string, string, KbGitExecResult>? ExecOverride { get; set; }

    public KbGitExecResult Run(string cwd, string args, int timeoutMs = 30_000)
    {
        if (ExecOverride is { } ov)
            return ov(cwd, args);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p is null)
            return new KbGitExecResult(-1, "", "git start failed");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return new KbGitExecResult(-1, "", "git timeout");
        }

        return new KbGitExecResult(
            p.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }
}
