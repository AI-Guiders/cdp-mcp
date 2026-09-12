#nullable enable
using Cdp.Config;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpOpsConfigTests
{
    [Fact]
    public void Bind_maps_tools_forum_tenant_ops_and_bridge()
    {
        var doc = CdpConfigLoader.Parse("""
            [bridge]
            composer = "debug"
            capabilities_poll_ms = 3000

            [tenant]
            idle_ttl_minutes = 90

            [ops]
            oom_wake_cdt_edge = true
            shell_ignite_arm = false

            [tools]
            rg = "D:/tools/rg.exe"
            plugins_root = "D:/plugins"
            openvsx_base = "http://ovsx.test"
            opencode_bin = "oc"
            opencode_directory = "D:/oc"

            [forum]
            root = "D:/forum"

            [deploy]
            script = "D:/deploy.ps1"
            """);

        var ops = CdpOpsConfig.Map(doc);

        Assert.Equal("D:/tools/rg.exe", ops.RgPath);
        Assert.Equal("D:/plugins", ops.PluginsRoot);
        Assert.Equal("http://ovsx.test", ops.OpenvsxBase);
        Assert.Equal("oc", ops.OpencodeBin);
        Assert.Equal("D:/oc", ops.OpencodeDirectory);
        Assert.Equal("D:/forum", ops.ForumRoot);
        Assert.Equal("D:/deploy.ps1", ops.DeployScript);
        Assert.Equal(90, ops.TenantIdleTtlMinutes);
        Assert.True(ops.OomWakeCdtEdge);
        Assert.False(ops.ShellIgniteArm);
        Assert.Equal("debug", ops.BridgeComposer);
        Assert.Equal(3000, ops.BridgeCapabilitiesPollMs);
    }
}
