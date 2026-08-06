using System.Text.Json;
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

    [Fact]
    public void TryProcessOnce_autonomous_on_clears_folded_await_partner()
    {
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("resume")
        });
        var id = "test-folded-autoi-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("folded await"),
            ["last_once"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });
        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.LastOnce = true;
                a.Once = true;
                a.Status = "awaiting";
                a.Event = "halt";
                a.FiredUtc = DateTimeOffset.UtcNow;
            }));
            IdeIgniteArmHost.SetAutonomous(false, "test_folded");
            IdeIgniteArmHost.PublishGlass();
            Assert.Contains(IdeIgniteArmHost.Snapshot(), a => a.Id == id && a.Status == "awaiting");

            GlassIgniteCmdBridge.ResetProcessedForTests();
            File.WriteAllText(
                GlassIgniteCmdBridge.RequestPath,
                "{\"schema\":\"glass_ignite_cmd/v0\",\"origin\":\"glass\",\"id\":\"fold456\",\"op\":\"autonomous_on\"}");

            Assert.True(GlassIgniteCmdBridge.TryProcessOnce());
            Assert.True(IdeIgniteArmHost.IsAutonomousArmed());
            Assert.DoesNotContain(IdeIgniteArmHost.Snapshot(), a => a.Status is "awaiting");
            Assert.DoesNotContain(IdeIgniteArmHost.Snapshot(), a => a.Id == id);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("disarm"),
                ["id"] = JsonSerializer.SerializeToElement(id),
                ["force"] = JsonSerializer.SerializeToElement(true)
            });
        }
    }
}
