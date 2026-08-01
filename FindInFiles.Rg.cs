using System.Diagnostics;
using System.Text;

namespace CdpMcp;

/// <summary>ripgrep process runner for Find in Files (≤ADX soft-warn peel).</summary>
internal static partial class FindInFiles
{
    static bool TryRunRg(
        string rg,
        List<string> argv,
        string cwd,
        int timeoutMs,
        out string stdout,
        out string stderr,
        out int exit,
        out string? error)
    {
        stdout = "";
        stderr = "";
        exit = -1;
        error = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = rg,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in argv)
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null)
            {
                error = "process_start_null";
                return false;
            }

            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                error = "timeout";
                return false;
            }

            stdout = outTask.GetAwaiter().GetResult();
            stderr = errTask.GetAwaiter().GetResult();
            exit = p.ExitCode;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string? ResolveRg()
    {
        var env = Environment.GetEnvironmentVariable("CDP_RG");
        if (env is { Length: > 0 } && File.Exists(env))
            return env;

        // Prefer PATH resolution via where/where.exe semantics — try common names.
        foreach (var name in new[] { "rg.exe", "rg" })
        {
            var hit = FindOnPath(name);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // ignore bad PATH entries
            }
        }

        return null;
    }
}
