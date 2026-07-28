#nullable enable
using System.Diagnostics;

namespace CdpMcp.Cockpit.DataAcquisition;

/// <summary>
/// DAL locus: resolve toolchain binaries on PATH (ADR 0102 / 0198).
/// External I/O only — channel/CCU consume results, do not probe here.
/// </summary>
public static class ToolchainPathProbe
{
    public static string? Resolve(string bin)
    {
        try
        {
            if (Path.IsPathRooted(bin) && File.Exists(bin))
                return bin;

            var name = bin;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                OperatingSystem.IsWindows())
                name += ".exe";

            RefreshProcessPath();
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                    var bare = Path.Combine(dir.Trim(), bin);
                    if (File.Exists(bare))
                        return bare;
                }
                catch
                {
                    /* skip bad PATH entry */
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                Arguments = bin,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (p.ExitCode != 0) return null;
            var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Recompose process PATH from Machine+User (+ process-only extras) so winget/scoop installs are visible without MCP remount.</summary>
    public static void RefreshProcessPath()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Environment.SetEnvironmentVariable("PATH", ComposePathEnv(), EnvironmentVariableTarget.Process);
    }

    static string ComposePathEnv()
    {
        if (!OperatingSystem.IsWindows())
            return Environment.GetEnvironmentVariable("PATH") ?? "";

        var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var process = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        void Add(string block)
        {
            foreach (var raw in block.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = raw.Trim();
                if (dir.Length == 0 || !seen.Add(dir))
                    continue;
                parts.Add(dir);
            }
        }

        Add(machine);
        Add(user);
        Add(process);
        return string.Join(Path.PathSeparator, parts);
    }

}
