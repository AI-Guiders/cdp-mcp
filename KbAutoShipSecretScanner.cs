using System.Diagnostics;

namespace CdpMcp;

internal readonly record struct KbSecretScanResult(bool Blocked, string? Detail, bool ScannerMissing);

/// <summary>
/// CDP-ADR-0236: content-based secret gate for KB AutoShip — gitleaks over the working tree.
/// Replaces the filename heuristic (which could not catch real secrets in innocently-named
/// files, and could not ship the deletion of a secret-named path).
/// Missing scanner fails OPEN: an absent tool must not brick the shipper of a personal KB.
/// </summary>
internal static class KbAutoShipSecretScanner
{
    internal static Func<string, string, KbGitExecResult>? ExecOverride { get; set; }

    // Resolved executable ("gitleaks" via PATH or an absolute winget package path); "" = probed and absent.
    static string? _resolved;

    public static KbSecretScanResult Scan(string repoRoot, KbAutoShipOptions options)
    {
        if (string.Equals(options.SecretScanner, "off", StringComparison.OrdinalIgnoreCase))
            return new(false, null, false);

        var exe = Resolve(options.SecretScanner);
        if (exe is null)
            return new(false, null, ScannerMissing: true);

        var args = "detect --no-git --redact --exit-code 1";
        var config = string.IsNullOrWhiteSpace(options.SecretScannerConfig)
            ? Path.Combine(repoRoot, ".gitleaks.toml")
            : Path.GetFullPath(options.SecretScannerConfig.Trim());
        if (File.Exists(config))
            args += " --config " + Quote(config);

        var run = Exec(exe, repoRoot, args, timeoutMs: 120_000);
        return run.ExitCode switch
        {
            0 => new(false, null, false),
            1 => new(true, $"gitleaks finding(s) in working tree (redacted):\n{Trim(run.Stdout)}", false),
            _ => new(true, $"gitleaks error (exit {run.ExitCode}): {Trim(run.Stderr)}", false),
        };
    }

    internal static void ResetResolutionCache() => _resolved = null;

    static string? Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting)
            && !string.Equals(setting, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var explicitPath = Path.GetFullPath(setting.Trim());
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        if (_resolved is null)
        {
            // PATH first; the winget portable location second — the CDP host process
            // outlives the PATH refresh that follows an installation.
            _resolved = Exec("gitleaks", Environment.CurrentDirectory, "version", 15_000).ExitCode == 0
                ? "gitleaks"
                : ProbeWingetPackage() ?? "";
        }

        return string.IsNullOrEmpty(_resolved) ? null : _resolved;
    }

    static string? ProbeWingetPackage()
    {
        try
        {
            var packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            return Directory.EnumerateDirectories(packages, "Gitleaks.Gitleaks_*")
                .Select(d => Path.Combine(d, "gitleaks.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    static KbGitExecResult Exec(string fileName, string cwd, string args, int timeoutMs)
    {
        if (ExecOverride is { } ov)
            return ov(fileName, args);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception ex)
        {
            // A missing executable throws (does not return null) — e.g. the "gitleaks"
            // PATH probe on a host whose PATH predates the installation.
            return new KbGitExecResult(-1, "", $"{fileName} start failed: {ex.Message}");
        }

        if (p is null)
            return new KbGitExecResult(-1, "", $"{fileName} start failed");
        using (p)
        {
            try { p.StandardInput.Close(); } catch { /* stdin closed: no interactive input */ }

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new KbGitExecResult(-1, "", $"{fileName} timeout");
            }

            return new KbGitExecResult(
                p.ExitCode,
                stdoutTask.GetAwaiter().GetResult(),
                stderrTask.GetAwaiter().GetResult());
        }
    }

    static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    static string Trim(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "(no output)" : s.Trim()[..Math.Min(s.Trim().Length, 400)];
}
