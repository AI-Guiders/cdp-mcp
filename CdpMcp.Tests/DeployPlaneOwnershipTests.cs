using Xunit;

namespace CdpMcp.Tests;

public sealed class DeployPlaneOwnershipTests : IDisposable
{
    readonly string _dir;

    public DeployPlaneOwnershipTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(),
            "cdp-mcp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public void Lock_acquire_makes_it_fresh_then_release_clears()
    {
        Assert.False(Cdp.Deploy.CdpDeployLock.IsFresh(_dir));

        Cdp.Deploy.CdpDeployLock.Acquire(_dir, "test-job");
        Assert.True(Cdp.Deploy.CdpDeployLock.IsFresh(_dir));

        var body = File.ReadAllText(Cdp.Deploy.CdpDeployLock.PathFor(_dir));
        Assert.Contains("job_id=test-job", body, StringComparison.Ordinal);

        Cdp.Deploy.CdpDeployLock.Release(_dir);
        Assert.False(Cdp.Deploy.CdpDeployLock.IsFresh(_dir));
    }

    [Fact]
    public void Lock_expired_ttl_reports_not_fresh()
    {
        var lockPath = Cdp.Deploy.CdpDeployLock.PathFor(_dir);
        File.WriteAllText(lockPath, "utc=stale");
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow - TimeSpan.FromMinutes(6));

        Assert.False(Cdp.Deploy.CdpDeployLock.IsFresh(_dir));
    }

    [Fact]
    public void Clone_deploy_worker_copies_tree_and_returns_clone_exe()
    {
        var workerExe = Path.Combine(_dir, "CdpService.exe");
        var configDir = Path.Combine(_dir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(workerExe, "stub-exe");
        File.WriteAllText(Path.Combine(_dir, "CdpMcp.dll"), "stub-dll");
        File.WriteAllText(Path.Combine(configDir, "cdp-mcp.toml"), "port = 8771");

        var cloned = IdeLifecycleJobs.CloneDeployWorker(workerExe);

        var cloneDir = Path.GetDirectoryName(cloned)!;
        Assert.NotEqual(_dir, cloneDir, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("cdp-mcp", "workers"), cloneDir, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(cloned));
        Assert.True(File.Exists(Path.Combine(cloneDir, "CdpMcp.dll")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "config", "cdp-mcp.toml")));

        Directory.Delete(cloneDir, recursive: true);
    }

    [Fact]
    public void Clone_deploy_worker_falls_back_to_original_when_exe_cannot_copy()
    {
        var workerExe = Path.Combine(_dir, "CdpService.exe");
        File.WriteAllText(workerExe, "stub");
        using var held = File.Open(workerExe, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var cloned = IdeLifecycleJobs.CloneDeployWorker(workerExe);

        Assert.Equal(workerExe, cloned);
    }
}
