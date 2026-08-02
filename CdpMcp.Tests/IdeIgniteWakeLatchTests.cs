using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteWakeLatchTests : IDisposable
{
    readonly string _root;

    public IdeIgniteWakeLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-ignite-wake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeIgniteWakeLatch.RootOverrideForTests = _root;
        CideIntercomPresenceLatch.RootOverrideForTests = _root;
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        IdeIgniteWakeLatch.RootOverrideForTests = null;
        CideIntercomPresenceLatch.RootOverrideForTests = null;
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_writes_channel_and_charge()
    {
        var doc = IdeIgniteWakeLatch.Publish(
            "arm-1", "Resume habitat.", IdeIgniteWakeLatch.ChannelComposer, reason: "timer", task: "leaf");

        Assert.NotNull(doc);
        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.Schema, latch!.Schema);
        Assert.Equal("arm-1", latch.ArmId);
        Assert.Equal(IdeIgniteWakeLatch.ChannelComposer, latch.Channel);
        Assert.Equal("Resume habitat.", latch.Charge);
        Assert.Equal("timer", latch.Reason);
        Assert.Equal("leaf", latch.Task);
    }

    [Fact]
    public void IsHabitatPartnerLive_busy_pf_true()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        Assert.True(IdeIgniteArmHost.IsHabitatPartnerLive());
    }

    [Fact]
    public void TryDeliverHabitatWake_prefers_when_pf_busy_timer()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-habitat-1",
            Event = "timer",
            Status = "firing",
            Message = "wake",
            ChargeMode = "minimal",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        var result = IdeIgniteArmHost.TryDeliverHabitatWake(arm, "Resume from Task Manager.");
        Assert.NotNull(result);

        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelHabitat, latch!.Channel);
        Assert.Equal("arm-habitat-1", latch.ArmId);

        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("Resume from Task Manager.", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDeliverHabitatWake_skips_system_wake()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "remount-wake-abc",
            Event = "timer",
            Status = "firing",
            Message = "remount",
            ChargeMode = "remount",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "reason=remount"));
    }

    [Fact]
    public void MayPreferHabitatOverComposer_rejects_human_away()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-hild",
            Event = "human_away",
            Status = "armed",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };
        Assert.False(IdeIgniteArmHost.MayPreferHabitatOverComposer(arm));
    }
}
