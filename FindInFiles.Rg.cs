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

    /// <summary>
    /// Resolve rg for throw-Cursor / Citizen find — not Cursor PATH alone.
    /// Order: CDP_RG → PATH → habitat bin → beside exe → WinGet BurntSushi → Cursor vscode ripgrep (dogfood last).
    /// </summary>
    internal static string? ResolveRg()
    {
        var env = Environment.GetEnvironmentVariable("CDP_RG");
        if (env is { Length: > 0 } && File.Exists(env))
            return env;

        foreach (var name in new[] { "rg.exe", "rg" })
        {
            var hit = FindOnPath(name);
            if (hit is not null)
                return hit;
        }

        foreach (var candidate in HabitatRgCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    static IEnumerable<string> HabitatRgCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "cdp-mcp", "bin", "rg.exe");

        var baseDir = AppContext.BaseDirectory;
        if (baseDir is { Length: > 0 })
        {
            yield return Path.Combine(baseDir, "rg.exe");
            yield return Path.Combine(baseDir, "tools", "rg.exe");
        }

        foreach (var winget in EnumerateWinGetRg(local))
            yield return winget;

        // Dogfood last — throw-Cursor must not require Cursor install; keep as soft fallback.
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "cursor", "resources", "app", "node_modules", "@vscode", "ripgrep", "bin", "rg.exe");
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.Equals(pf, pf86, StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(pf86, "cursor", "resources", "app", "node_modules", "@vscode", "ripgrep", "bin", "rg.exe");
    }

    static IEnumerable<string> EnumerateWinGetRg(string localAppData)
    {
        var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packages))
            yield break;

        string[] roots;
        try
        {
            roots = Directory.GetDirectories(packages, "BurntSushi.ripgrep*");
        }
        catch
        {
            yield break;
        }

        foreach (var root in roots)
        {
            string[] hits;
            try
            {
                hits = Directory.GetFiles(root, "rg.exe", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var hit in hits)
                yield return hit;
        }
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
