using Xunit;

namespace CdpMcp.Tests;

public partial class IdeIgniteWakeLatchTests : IDisposable
{
    readonly string _root;

    public IdeIgniteWakeLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-ignite-wake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeIgniteWakeLatch.BootRefreshEnabled = false;
        IdeIgniteWakeLatch.RootOverrideForTests = _root;
        CideIntercomPresenceLatch.RootOverrideForTests = _root;
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        IdeIgniteWakeLatch.BootRefreshEnabled = true;
        IdeIgniteArmHost.BindAutonomous(null);
        IdeCitizenChannel.ResetAutoiWakeHooksForTests();
        IdeIgniteWakeLatch.RootOverrideForTests = null;
        CideIntercomPresenceLatch.RootOverrideForTests = null;
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void RefreshCanonicalIfStale_republishes_when_version_or_markers_stale()
    {
        var staleJson = """
            {
              "schema": "ignite_wake_latch/v0",
              "arm_id": "arm-stale",
              "channel": "composer",
              "charge": "do not rewrite for agent convenience",
              "course": "## operator_priority\n1. Glass Done (human flight)\n4. Glass + Citizen DEFERRED",
              "task": "F1b Forge Windows pilot Setup",
              "habitat_version": "0.0.1",
              "charge_template_rev": "stale",
              "stamped_utc": "2026-08-22T00:00:00+00:00"
            }
            """;
        File.WriteAllText(IdeIgniteWakeLatch.LatchPath, staleJson);

        var doc = IdeIgniteWakeLatch.RefreshCanonicalIfStale("test_refresh");
        Assert.NotNull(doc);
        Assert.Equal("arm-stale", doc!.ArmId);
        Assert.Equal("F1b Forge Windows pilot Setup", doc.Task);
        Assert.Contains("joint course", doc.Charge, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IdeIgniteWakeLatch.HabitatVersion(), doc.HabitatVersion);
        Assert.Equal(IdeIgniteChannel.ChargeTemplateRev, doc.ChargeTemplateRev);
        Assert.DoesNotContain("Glass Done (human flight)", doc.Course ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshCanonicalIfStale_skips_when_fresh()
    {
        var charge = IdeIgniteChannel.ComposeArmFireCharge();
        var course = IdePressureChannel.TryPeekSealedCourse();
        IdeIgniteWakeLatch.Publish(
            "arm-fresh", charge, IdeIgniteWakeLatch.ChannelComposer,
            reason: "seed", task: "leaf", course: course);

        var doc = IdeIgniteWakeLatch.RefreshCanonicalIfStale();
        Assert.NotNull(doc);
        Assert.Equal("arm-fresh", doc!.ArmId);
        Assert.Equal("seed", doc.Reason);
    }

    [Fact]
    public void RefreshProductionLatch_when_CDP_WAKE_REFRESH()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CDP_WAKE_REFRESH"), "1", StringComparison.Ordinal))
            return;

        IdeIgniteWakeLatch.RootOverrideForTests = null;
        IdePressureChannel.SealedCourseOverrideForTests = null;
        IdePressureChannel.TrySanitizeStashCourseOnBoot();

        var doc = IdeIgniteWakeLatch.RefreshCanonicalIfStale("wake_refresh");
        Assert.NotNull(doc);
        Assert.Contains("joint course", doc!.Charge!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do not rewrite for agent convenience", doc.Charge!, StringComparison.OrdinalIgnoreCase);
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
    public void TryDeliverHabitatWake_invite_ready_idle_pf_falls_through_to_composer()
    {
        // Cursor host: Composer is the gun — do not prefer_citizen steal here.
        IdeIgniteArmHost.BindAutonomous(true);
        IdeCitizenChannel.InviteReadyOverrideForTests = () => true;
        IdeCitizenChannel.AutoiWakeTurnOverrideForTests = charge =>
            new CitizenCompletions.TurnResult(
                Ok: true,
                Error: null,
                Hint: null,
                Text: "should not eat: " + charge,
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

        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "Resume from Task Manager."));

        var latch = IdeIgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(IdeIgniteWakeLatch.ChannelHabitat, latch!.Channel);
        Assert.Null(CideIntercomVoiceLatch.TryRead());
    }

    [Fact]
    public async Task TryDeliverHabitatWhenComposerUnavailable_citizen_consumes_when_invite_ready()
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
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-citizen-gone",
            Event = "timer",
            Status = "firing",
            Reason = "timer",
            Task = "leaf",
            Once = true,
            Port = 1,
            WaitSeconds = 1
        };

        var result = await IdeIgniteArmHost.TryDeliverHabitatWhenComposerUnavailableAsync(
            arm, "Resume from Task Manager.", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("prefer_citizen", result!.GetType().GetProperty("detail")!.GetValue(result));
        Assert.Equal("citizen", result.GetType().GetProperty("submit_kind")!.GetValue(result));

        var voice = CideIntercomVoiceLatch.TryRead();
        Assert.NotNull(voice);
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, voice!.Kind);
        Assert.Contains("citizen ate:", voice.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryDeliverHabitatWhenComposerUnavailable_defers_prefer_citizen_when_pf_busy()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        var citizenAte = false;
        IdeCitizenChannel.InviteReadyOverrideForTests = () => true;
        IdeCitizenChannel.AutoiWakeTurnOverrideForTests = charge =>
        {
            citizenAte = true;
            return new CitizenCompletions.TurnResult(
                Ok: true,
                Error: null,
                Hint: null,
                Text: "should not eat: " + charge,
                Model: "test",
                Provider: "mock",
                Built: null,
                WireIntents: null,
                Routes: null,
                DryRun: false);
        };
        CideIntercomPresenceLatch.PublishSeat("pf", "busy", ttlSeconds: 120);
        Assert.True(IdeIgniteArmHost.IsHabitatPartnerLive());

        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = "arm-citizen-face-busy",
            Event = "timer",
            Status = "firing",
            Reason = "timer",
            Task = "leaf",
            Once = true,
            Port = 1,
            WaitSeconds = 1
        };

        var result = await IdeIgniteArmHost.TryDeliverHabitatWhenComposerUnavailableAsync(
            arm, "Resume from Task Manager.", CancellationToken.None);
        Assert.Null(result);
        Assert.False(citizenAte);
        Assert.Null(CideIntercomVoiceLatch.TryRead());
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
    public void MayPreferHabitatOverComposer_rejects_intercom_voice_cannon()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IntercomVoiceCannonState.ArmIdFor("deadbeef"),
            Event = "timer",
            Status = "armed",
            Once = true,
            Port = 9222,
            WaitSeconds = 30
        };
        Assert.True(IdeIgniteArmHost.IsIntercomVoiceCannonArmId(arm.Id));
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
    public void MirrorTimerWakeToIntercom_remount_skips_when_pf_busy()
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

        // Prefer still skipped for remount; Face busy → mute Radio remount spam.
        Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "reason=remount"));
        Assert.False(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "reason=remount busy PF"));
        Assert.Null(CideIntercomVoiceLatch.TryRead());
    }

}
