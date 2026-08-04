using Xunit;

namespace CdpMcp.Tests;

public sealed class GlassIgniteCmdBridgeTests : IDisposable
{
    readonly string _root;

    public GlassIgniteCmdBridgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-ignite-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        GlassIgniteCmdBridge.RootOverrideForTests = _root;
        CideIgniteLatch.RootOverrideForTests = _root;
        GlassIgniteCmdBridge.Stop();
        GlassIgniteCmdBridge.ResetProcessedForTests();
        IdeIgniteArmHost.SetAutonomous(false, "test_setup");
        IdeIgniteArmHost.SetHild(false, "test_setup");
    }

    public void Dispose()
    {
        GlassIgniteCmdBridge.Stop();
        GlassIgniteCmdBridge.RootOverrideForTests = null;
        CideIgniteLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void TryProcessOnce_autonomous_on()
    {
        File.WriteAllText(
            GlassIgniteCmdBridge.RequestPath,
            "{\"schema\":\"glass_ignite_cmd/v0\",\"origin\":\"glass\",\"id\":\"abc123\",\"op\":\"autonomous_on\"}");

        Assert.True(GlassIgniteCmdBridge.TryProcessOnce());
        Assert.True(IdeIgniteArmHost.IsAutonomousArmed());
        Assert.False(GlassIgniteCmdBridge.TryProcessOnce());
    }
}
