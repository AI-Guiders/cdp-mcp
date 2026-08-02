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
        IdeIgniteArmHost.BindAutonomous(null);
        IdeCitizenChannel.ResetAutoiWakeHooksForTests();
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
        IdeIgniteArmHost.BindAutonomous(false);
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
        Assert.Equal("prefer_duplex", result!.GetType().GetProperty("detail")!.GetValue(result));

        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelHabitat, latch!.Channel);
        Assert.Equal("arm-habitat-1", latch.ArmId);

        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("Resume from Task Manager.", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDeliverHabitatWake_stamps_habitat_ssot_when_autonomous_idle_pf_but_falls_through()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeCitizenChannel.InviteReadyOverrideForTests = () => false;
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-habitat-auto",
            Event = "timer",
            Status = "firing",
            Message = "wake",
            ChargeMode = "minimal",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        // Guest Autoi residual: stamp habitat SSOT, return null so CDT still injects.
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "Resume autonomous habitat."));

        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelHabitat, latch!.Channel);
        Assert.Equal("arm-habitat-auto", latch.ArmId);
        Assert.True(IdeIgniteWakeLatch.IsHabitatLatchForArm(arm.Id));

        // Intercom deferred to MirrorTimerWakeToIntercom on CDT fallthrough.
        Assert.Null(CideIntercomVoiceLatch.TryRead());
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "Resume autonomous habitat."));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("Resume autonomous habitat.", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDeliverHabitatWake_citizen_consumes_when_invite_ready_idle_pf()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeCitizenChannel.InviteReadyOverrideForTests = () => true;
        IdeCitizenChannel.AutoiWakeTurnOverrideForTests = charge =>
            new CitizenCompletions.TurnResult(
                Ok: true,
                Error: null,
                Hint: null,
                Text: "citizen ate: " + charge,
                Model: "test",
                Provider: "mock",
                Built: null,
                WireIntents: null,
                Routes: null,
                DryRun: false);
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-habitat-citizen",
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
        Assert.Equal("prefer_citizen", result!.GetType().GetProperty("detail")!.GetValue(result));
        Assert.Equal("citizen", result.GetType().GetProperty("submit_kind")!.GetValue(result));

        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelHabitat, latch!.Channel);

        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, voice!.Kind);
        Assert.Contains("citizen ate:", voice.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDeliverHabitatWake_null_when_partner_mode_idle_pf()
    {
        IdeIgniteArmHost.BindAutonomous(false);
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-habitat-partner-idle",
            Event = "timer",
            Status = "firing",
            Message = "wake",
            ChargeMode = "minimal",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "should fall through to Composer"));
        Assert.Null(IdeIgniteWakeLatch.TryRead());
    }

    [Fact]
    public void TryDeliverHabitatWake_skips_system_wake()
    {
        IdeIgniteArmHost.BindAutonomous(true);
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

    [Fact]
    public void MirrorTimerWakeToIntercom_when_pf_idle_publishes_voice()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-mirror-1",
            Event = "timer",
            Status = "firing",
            Message = "wake",
            ChargeMode = "minimal",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "Resume idle-PF mirror."));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("Resume idle-PF mirror.", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_skips_when_pf_busy()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-mirror-busy",
            Event = "timer",
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.False(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "should not mirror"));
        Assert.Null(CideIntercomVoiceLatch.TryRead());
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_remount_publishes_when_pf_idle()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "remount-wake-xyz",
            Event = "timer",
            Status = "firing",
            ChargeMode = "remount",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.IsRemountWakeArm(arm));
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=remount"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("reason=remount", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_remount_publishes_when_pf_busy()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "remount-wake-busy",
            Event = "timer",
            Status = "firing",
            ChargeMode = "remount",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        // Prefer still skipped for remount; mirror is the residual.
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "reason=remount"));
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=remount busy PF"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("reason=remount busy PF", voice!.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stop", true)]
    [InlineData("queue", true)]
    [InlineData("STOP", true)]
    [InlineData("voice", false)]
    [InlineData("send", false)]
    public void IsComposerBusyKind_stop_or_queue(string kind, bool expected) =>
        Assert.Equal(expected, IdeIgniteArmHost.IsComposerBusyKind(kind));

    [Theory]
    [InlineData(true, "stop", true)]
    [InlineData(true, "queue", true)]
    [InlineData(true, "voice", false)]
    [InlineData(true, "send", false)]
    [InlineData(false, "no_composer", true)]
    [InlineData(false, "down", true)]
    [InlineData(true, "no_composer", true)]
    [InlineData(true, "down", true)]
    public void ShouldSkipCdtAfterIntercomMirror_busy_or_gone(bool sampleOk, string kind, bool expected) =>
        Assert.Equal(expected, IdeIgniteArmHost.ShouldSkipCdtAfterIntercomMirror(sampleOk, kind));

    [Fact]
    public async Task TryDeliverMirroredWhenComposerBusy_skips_when_not_mirrored()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "remount-wake-not-mirrored",
            Event = "timer",
            Status = "firing",
            ChargeMode = "remount",
            Once = true,
            Port = 9222,
            WaitSeconds = 5
        };
        Assert.Null(await IdeIgniteArmHost.TryDeliverMirroredWhenComposerBusyAsync(
            arm, "reason=remount", intercomMirrored: false, CancellationToken.None));
    }

    [Fact]
    public async Task TryDeliverMirroredWhenComposerBusy_skips_when_not_mirrored_work_arm()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-work",
            Event = "timer",
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 5
        };
        Assert.Null(await IdeIgniteArmHost.TryDeliverMirroredWhenComposerBusyAsync(
            arm, "resume", intercomMirrored: false, CancellationToken.None));
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_escalate_publishes_when_pf_busy()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeIgniteArmHost.HildEscalateArmId,
            Event = "timer",
            Status = "firing",
            ChargeMode = IdeIgniteArmHost.HildEscalateChargeMode,
            Reason = IdeIgniteArmHost.HildEscalateReason,
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.IsHildEscalateWakeArm(arm));
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "reason=escalate"));
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=escalate busy PF"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("reason=escalate busy PF", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_escalate_publishes_when_pf_idle()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeIgniteArmHost.HildEscalateArmId,
            Event = "timer",
            Status = "firing",
            ChargeMode = IdeIgniteArmHost.HildEscalateChargeMode,
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=escalate"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("reason=escalate", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_oom_publishes_when_pf_busy()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "oom-wake-xyz",
            Event = "timer",
            Status = "firing",
            ChargeMode = IdeOomWake.ChargeMode,
            Reason = IdeOomWake.Reason,
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.IsOomWakeArm(arm));
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "reason=oom"));
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=oom busy PF"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("reason=oom busy PF", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_oom_publishes_when_pf_idle()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "oom-wake-xyz",
            Event = "timer",
            Status = "firing",
            ChargeMode = IdeOomWake.ChargeMode,
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=oom"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("reason=oom", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_tool_wake_publishes_when_pf_busy()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "tool-wake-abc",
            Event = "timer",
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.IsToolWakeArmId(arm.Id));
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "tool still running"));
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "tool still running busy PF"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("tool still running busy PF", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_tool_wake_publishes_when_pf_idle()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "tool-wake-xyz",
            Event = "timer",
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "tool still running"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("tool still running", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_hild_away_publishes_when_pf_busy()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeIgniteArmHost.HildAwayArmId,
            Event = "human_away",
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.IsHildAwayWakeArm(arm));
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "HILD human_away"));
        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "HILD human_away busy PF"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("HILD human_away busy PF", voice!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_hild_away_publishes_when_pf_idle()
    {
        Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeIgniteArmHost.HildAwayArmId,
            Event = "human_away",
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };

        Assert.True(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "HILD human_away"));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("HILD human_away", voice!.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("arm-work", "timer", true)]
    [InlineData("remount-wake-x", "timer", true)]
    [InlineData("tool-wake-x", "timer", true)]
    [InlineData("hild-away", "human_away", true)]
    [InlineData("arm-build", "build_finished", false)]
    [InlineData("arm-test", "test_finished", false)]
    [InlineData("arm-shell", "shell_finished", false)]
    public void MayDeliverHabitatWhenComposerUnavailable_gates(string id, string ev, bool expected)
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = id,
            Event = ev,
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 5
        };
        Assert.Equal(expected, IdeIgniteArmHost.MayDeliverHabitatWhenComposerUnavailable(arm));
    }

    [Fact]
    public async Task TryDeliverHabitatWhenComposerUnavailable_delivers_when_sample_down_without_mirror()
    {
        // No CDT → TrySampleComposerAsync returns (false, down) → ShouldSkip → habitat.
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-no-mirror",
            Event = "timer",
            Status = "firing",
            Reason = "timer",
            Task = "leaf",
            Once = true,
            Port = 1,
            WaitSeconds = 1
        };

        var result = await IdeIgniteArmHost.TryDeliverHabitatWhenComposerUnavailableAsync(
            arm, "resume without mirror", CancellationToken.None);
        Assert.NotNull(result);
        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelHabitat, latch!.Channel);
        Assert.Equal("resume without mirror", latch.Charge);
        Assert.Equal("idle_pf_composer_gone", result!.GetType().GetProperty("detail")!.GetValue(result));
        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Contains("resume without mirror", voice!.Body, StringComparison.Ordinal);
    }

[Theory]
    [InlineData("arm-work", "timer", true, "stop", true, false, false)]
    [InlineData("arm-work", "timer", true, "queue", true, false, false)]
    [InlineData("arm-work", "timer", true, "stop", false, false, true)]
    [InlineData("arm-work", "timer", true, "stop", true, true, true)]
    [InlineData("arm-work", "timer", true, "down", true, false, true)]
    [InlineData("arm-work", "timer", true, "voice", true, false, false)]
    [InlineData("remount-wake-x", "timer", true, "stop", true, false, true)]
    [InlineData("hild-escalate-x", "timer", true, "stop", true, false, true)]
    public void ShouldHabitatSkipWhenComposerUnavailable_guest_autoi_busy_falls_through(
        string id, string ev, bool sampleOk, string kind, bool autonomous, bool duplex, bool expect)
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = id,
            Event = ev,
            Status = "firing",
            Once = true,
            Port = 9222,
            WaitSeconds = 5
        };
        Assert.Equal(
            expect,
            IdeIgniteArmHost.ShouldHabitatSkipWhenComposerUnavailable(
                arm, sampleOk, kind, autonomous, duplex));
    }

        [Fact]
    public async Task TryDeliverHabitatWhenComposerUnavailable_skips_build_finished()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-build",
            Event = "build_finished",
            Status = "firing",
            Once = true,
            Port = 1,
            WaitSeconds = 1
        };
        Assert.Null(await IdeIgniteArmHost.TryDeliverHabitatWhenComposerUnavailableAsync(
            arm, "build wake", CancellationToken.None));
        Assert.Null(IdeIgniteWakeLatch.TryRead());
    }
}
