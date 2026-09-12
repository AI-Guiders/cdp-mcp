#nullable enable
using Cdp.Deploy;
using System.Text.Json.Nodes;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpBridgeRevNudgeTests
{
    [Fact]
    public void BumpEntry_sets_bridge_rev_and_strips_legacy_env()
    {
        var entry = JsonNode.Parse("""
            {
              "command": "CdpMcpBridge.exe",
              "args": ["--config", "D:/cdp-mcp/cdp-mcp.toml"],
              "env": { "CDP_RELOAD_NUDGE": "old" }
            }
            """) as JsonObject;

        Assert.NotNull(entry);
        Assert.True(CdpBridgeRevNudge.BumpEntry(entry, "20260912-150000"));

        var args = entry["args"]!.AsArray();
        Assert.Equal("--bridge-rev", args[2]!.GetValue<string>());
        Assert.Equal("20260912-150000", args[3]!.GetValue<string>());
        Assert.Null(entry["env"]);
    }
}

public sealed class CdpServiceHostSlotPortTests
{
    [Fact]
    public void ResolveSlotPort_reads_cli_flag_when_port_free()
    {
        var pinned = CdpSlotRegistry.PickFreePort();
        var port = CdpServiceHost.ResolveSlotPort(["--slot-port", pinned.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        Assert.Equal(pinned, port);
    }
}
