using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;
public partial class IdeIgniteArmHostTests
{
    [Fact]
    public void Arm_timer_keeps_event_wakes_and_system_wakes()
    {
        var remount = IdeRemountWake.ArmIdPrefix + Guid.NewGuid().ToString("N")[..8];
        var build = "test-build-" + Guid.NewGuid().ToString("N")[..8];
        var testEv = "test-test-" + Guid.NewGuid().ToString("N")[..8];
        var firing = "test-firing-" + Guid.NewGuid().ToString("N")[..8];
        var next = "test-arm-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(remount), ["task"] = JsonSerializer.SerializeToElement("remount-initialized"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("build_finished"), ["id"] = JsonSerializer.SerializeToElement(build), ["task"] = JsonSerializer.SerializeToElement("build wake"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("test_finished"), ["id"] = JsonSerializer.SerializeToElement(testEv), ["task"] = JsonSerializer.SerializeToElement("test wake"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(firing), ["task"] = JsonSerializer.SerializeToElement("mid cdt"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            Assert.True(IdeIgniteArmHost.TryMutateForTests(firing, a => a.Status = "firing"));
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("30m"), ["id"] = JsonSerializer.SerializeToElement(next), ["task"] = JsonSerializer.SerializeToElement("second"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            var ids = IdeIgniteArmHost.Snapshot().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains(remount, ids);
            Assert.Contains(build, ids);
            Assert.Contains(testEv, ids);
            Assert.Contains(firing, ids);
            Assert.Contains(next, ids);
            Assert.Equal("firing", IdeIgniteArmHost.Snapshot().First(a => a.Id == firing).Status);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["all"] = JsonSerializer.SerializeToElement(true) });
        }
    }

    [Fact]
    public void Arm_timer_keeps_remount_wake_and_does_not_kill_firing()
    {
        var remount = IdeRemountWake.ArmIdPrefix + Guid.NewGuid().ToString("N")[..8];
        var firing = "test-firing-" + Guid.NewGuid().ToString("N")[..8];
        var next = "test-arm-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(remount), ["task"] = JsonSerializer.SerializeToElement("remount-initialized"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(firing), ["task"] = JsonSerializer.SerializeToElement("mid cdt"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            Assert.True(IdeIgniteArmHost.TryMutateForTests(firing, a => a.Status = "firing"));
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("30m"), ["id"] = JsonSerializer.SerializeToElement(next), ["task"] = JsonSerializer.SerializeToElement("second"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            var ids = IdeIgniteArmHost.Snapshot().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains(remount, ids);
            Assert.Contains(firing, ids);
            Assert.Contains(next, ids);
            Assert.Equal("firing", IdeIgniteArmHost.Snapshot().First(a => a.Id == firing).Status);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["all"] = JsonSerializer.SerializeToElement(true) });
        }
    }

    [Fact]
    public void Arm_timer_replaces_prior_continuity_timer_keeps_tool_wake()
    {
        var first = "test-arm-" + Guid.NewGuid().ToString("N")[..8];
        var second = "test-arm-" + Guid.NewGuid().ToString("N")[..8];
        var wake = "tool-wake-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(first), ["task"] = JsonSerializer.SerializeToElement("first"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("2h"), ["id"] = JsonSerializer.SerializeToElement(wake), ["task"] = JsonSerializer.SerializeToElement("tool hang"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("30m"), ["id"] = JsonSerializer.SerializeToElement(second), ["task"] = JsonSerializer.SerializeToElement("second"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            var listJson = JsonSerializer.Serialize(IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("list") }));
            Assert.DoesNotContain(first, listJson, StringComparison.Ordinal);
            Assert.Contains(second, listJson, StringComparison.Ordinal);
            Assert.Contains(wake, listJson, StringComparison.Ordinal);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["all"] = JsonSerializer.SerializeToElement(true) });
        }
    }

    [Fact]
    public void Disarm_cancels_in_flight_fire_token()
    {
        var id = "tool-wake-" + Guid.NewGuid().ToString("N")[..8];
        var cts = IdeIgniteArmHost.AttachFireTokenForTests(id);
        try
        {
            Assert.False(cts.IsCancellationRequested);
            var disarm = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement> { ["id"] = JsonSerializer.SerializeToElement(id) });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(disarm));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(cts.IsCancellationRequested);
        }
        finally
        {
            IdeIgniteArmHost.CancelInFlightFire(id);
        }
    }
}