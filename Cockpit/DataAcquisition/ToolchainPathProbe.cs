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
}
