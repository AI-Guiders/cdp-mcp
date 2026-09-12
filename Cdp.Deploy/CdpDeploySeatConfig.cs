namespace Cdp.Deploy;

/// <summary>
/// Install-seat operator config — SSOT is <c>{seat}/cdp-mcp.toml</c> (not repo <c>config/</c> template).
/// </summary>
public static class CdpDeploySeatConfig
{
    public const string FileName = "cdp-mcp.toml";
    public const string DevConfigSubdir = "config";

    public static string SeatConfigPath(string installRoot) =>
        Path.Combine(installRoot, FileName);

    public static string DevTemplatePath(string deployRoot) =>
        Path.Combine(deployRoot, DevConfigSubdir, FileName);

    public static string? ResolveSeatConfigPath(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return null;

        var root = SeatConfigPath(installRoot);
        if (File.Exists(root))
            return root;

        var nested = DevTemplatePath(installRoot);
        return File.Exists(nested) ? nested : null;
    }

    /// <summary>Copy operator config from live seat into staged publish root (seat root only).</summary>
    public static void SeedFromLiveSeat(string liveInstallRoot, string stagedRoot)
    {
        if (string.IsNullOrWhiteSpace(liveInstallRoot) || string.IsNullOrWhiteSpace(stagedRoot))
            return;

        var source = ResolveSeatConfigPath(liveInstallRoot);
        if (source is null)
            return;

        Directory.CreateDirectory(stagedRoot);
        CopyWithTransientRetry(source, Path.Combine(stagedRoot, FileName));
    }

    /// <summary>Remove repo dev template shipped under <c>config/</c> — not operator SSOT.</summary>
    public static void StripDevTemplate(string deployRoot)
    {
        var nested = DevTemplatePath(deployRoot);
        if (!File.Exists(nested))
            return;

        File.Delete(nested);
        TryDeleteEmptyConfigDir(deployRoot);
    }

    /// <summary>After promote: keep root SSOT, migrate legacy <c>config/</c> only when root is missing.</summary>
    public static void NormalizeInstallSeat(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return;

        var root = SeatConfigPath(installRoot);
        var nested = DevTemplatePath(installRoot);

        if (!File.Exists(root) && File.Exists(nested))
            CopyWithTransientRetry(nested, root, overwrite: false);

        if (File.Exists(root) && File.Exists(nested))
        {
            File.Delete(nested);
            TryDeleteEmptyConfigDir(installRoot);
        }
    }

    static void CopyWithTransientRetry(string source, string dest, bool overwrite = true, int attempts = 8)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                if (!overwrite && File.Exists(dest))
                    return;
                File.Copy(source, dest, overwrite);
                return;
            }
            catch (IOException ex) when (i < attempts - 1 && IsTransientLock(ex))
            {
                Thread.Sleep(Math.Min(800, 60 * (i + 1)));
            }
            catch (UnauthorizedAccessException ex) when (i < attempts - 1 && IsTransientLock(ex))
            {
                Thread.Sleep(Math.Min(800, 60 * (i + 1)));
            }
        }
    }

    static bool IsTransientLock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
                || m.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void TryDeleteEmptyConfigDir(string deployRoot)
    {
        var dir = Path.Combine(deployRoot, DevConfigSubdir);
        if (!Directory.Exists(dir))
            return;

        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
    }
}
