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
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Сервис работает без терминала: интерактивный запрос (credential / host-key / passphrase)
        // превратился бы в молчаливое зависание до лимита и выглядел бы как «git timeout».
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_SSH_COMMAND"] =
            "ssh -o BatchMode=yes -o ConnectTimeout=8 -o ServerAliveInterval=5 -o ServerAliveCountMax=3";

        using var p = Process.Start(psi);
        if (p is null)
            return new KbGitExecResult(-1, "", "git start failed");
        try { p.StandardInput.Close(); } catch { /* stdin закрыт: ввод невозможен */ }

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
