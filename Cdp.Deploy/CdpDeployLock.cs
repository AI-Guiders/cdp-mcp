using System.Text;

namespace Cdp.Deploy;

/// <summary>
/// ADR-0211 deploy-plane ownership: file lock that fences auto-start (bridge ensurer,
/// service control) while a promote runs. Time-fenced (TTL) so a crashed worker cannot
/// wedge the plane forever — the lock is advisory-but-enforced by every starter.
/// </summary>
public static class CdpDeployLock
{
    static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(5);

    public static string PathFor(string installDir) => Path.Combine(installDir, "deploy.lock");

    public static void Acquire(string installDir, string jobId)
    {
        Directory.CreateDirectory(installDir);
        var body = new StringBuilder()
            .AppendLine($"utc={DateTimeOffset.UtcNow:O}")
            .AppendLine($"job_id={jobId}")
            .Append($"pid={Environment.ProcessId}");
        File.WriteAllText(PathFor(installDir), body.ToString());
    }

    public static void Release(string installDir)
    {
        try
        {
            File.Delete(PathFor(installDir));
        }
        catch
        {
            /* best effort — TTL fence handles leftovers */
        }
    }

    /// <summary>True while a promote is (probably) running: lock exists and is younger than the TTL.</summary>
    public static bool IsFresh(string installDir)
    {
        var path = PathFor(installDir);
        if (!File.Exists(path))
            return false;
        return DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path) < FreshWindow;
    }
}
