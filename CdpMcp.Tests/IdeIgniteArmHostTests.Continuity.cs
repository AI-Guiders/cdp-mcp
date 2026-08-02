using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;
public partial class IdeIgniteArmHostTests
{
    [Fact]
    public void WakeAfterHardDeploy_ok_and_reclaim_stuck_firing()
    {
        var id = "test-reclaim-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(id), ["task"] = JsonSerializer.SerializeToElement("reclaim probe"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.Status = "firing";
                a.Once = false; // recurring — reclaim mid-fire
                a.DueUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            }));
            var wake = IdeIgniteArmHost.WakeAfterHardDeploy();
            using var wdoc = JsonDocument.Parse(JsonSerializer.Serialize(wake));
            Assert.True(wdoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("wake_after_hard_deploy", wdoc.RootElement.GetProperty("op").GetString());
            Assert.True(wdoc.RootElement.GetProperty("reclaimed").GetInt32() >= 1);
            var snap = IdeIgniteArmHost.Snapshot().First(a => a.Id == id);
            Assert.Equal("armed", snap.Status);
            Assert.True(snap.DueUtc is { } d && d > DateTimeOffset.UtcNow.AddSeconds(-1));
            Assert.Contains("reclaimed", snap.LastError ?? "", StringComparison.Ordinal);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["id"] = JsonSerializer.SerializeToElement(id) });
        }
    }

    [Fact]
    public void ReclaimOverdue_requeues_once_stuck_firing_when_send_not_ok()
    {
        var id = "test-once-zombie-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(id), ["task"] = JsonSerializer.SerializeToElement("once zombie"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.Once = true;
                a.Status = "firing";
                a.SendOk = null; // remount mid wait-idle
                a.FiredUtc = null;
                a.DueUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            }));
            var reclaimed = IdeIgniteArmHost.ReclaimOverdue(TimeSpan.FromSeconds(1));
            Assert.Contains(id, reclaimed);
            var snap = IdeIgniteArmHost.Snapshot().First(a => a.Id == id);
            Assert.Equal("armed", snap.Status);
            Assert.Contains("reclaimed_stuck_firing", snap.LastError ?? "", StringComparison.Ordinal);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["id"] = JsonSerializer.SerializeToElement(id) });
        }
    }

    [Fact]
    public void ReclaimOverdue_drops_once_stuck_firing_when_send_ok()
    {
        var id = "test-once-sent-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(id), ["task"] = JsonSerializer.SerializeToElement("once sent"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.Once = true;
                a.Status = "firing";
                a.SendOk = true;
                a.FiredUtc = DateTimeOffset.UtcNow.AddSeconds(-30);
                a.DueUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            }));
            var reclaimed = IdeIgniteArmHost.ReclaimOverdue(TimeSpan.FromSeconds(1));
            Assert.DoesNotContain(id, reclaimed);
            Assert.DoesNotContain(IdeIgniteArmHost.Snapshot(), a => a.Id == id);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["id"] = JsonSerializer.SerializeToElement(id) });
        }
    }

    [Fact]
    public void Hygiene_removes_error_keeps_armed()
    {
        var keep = "test-hygiene-keep-" + Guid.NewGuid().ToString("N")[..8];
        var drop = "test-hygiene-drop-" + Guid.NewGuid().ToString("N")[..8];
        // Continuity timer re-arm supersedes prior timers — use mixed events so both coexist.
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("build_finished"), ["id"] = JsonSerializer.SerializeToElement(keep), ["task"] = JsonSerializer.SerializeToElement("keep continuity"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(drop), ["task"] = JsonSerializer.SerializeToElement("stale noise"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(drop, a =>
            {
                a.Status = "error";
                a.LastError = "fire_failed";
            }));
            var hygiene = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("hygiene") });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(hygiene));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("hygiene", doc.RootElement.GetProperty("op").GetString());
            var ids = IdeIgniteArmHost.Snapshot().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains(keep, ids);
            Assert.DoesNotContain(drop, ids);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["id"] = JsonSerializer.SerializeToElement(keep) });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["id"] = JsonSerializer.SerializeToElement(drop) });
        }
    }

    [Fact]
    public void StorePath_is_seat_scoped()
    {
        Assert.Contains("ignite-arms-", IdeIgniteArmHost.StorePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "ignite-arms.json", IdeIgniteArmHost.StorePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(IdeIgniteArmHost.Seat));
    }

    [Fact]
    public void LastOnce_latches_awaiting_and_blocks_repeat()
    {
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.Fly);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("resume") });
        var id = "test-last-once-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("1h"), ["id"] = JsonSerializer.SerializeToElement(id), ["task"] = JsonSerializer.SerializeToElement("await op"), ["last_once"] = JsonSerializer.SerializeToElement(true), ["force"] = JsonSerializer.SerializeToElement(true), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.LastOnce = true;
                a.Once = true;
                a.Status = "awaiting";
                a.FiredUtc = DateTimeOffset.UtcNow;
            }));
            var skip = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("arm"), ["when"] = JsonSerializer.SerializeToElement("timer"), ["in"] = JsonSerializer.SerializeToElement("6s"), ["task"] = JsonSerializer.SerializeToElement("repeat idle"), ["last_once"] = JsonSerializer.SerializeToElement(true), ["message"] = JsonSerializer.SerializeToElement("should skip"), ["settle_seconds"] = JsonSerializer.SerializeToElement(0) });
            using var sdoc = JsonDocument.Parse(JsonSerializer.Serialize(skip));
            Assert.True(sdoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(sdoc.RootElement.GetProperty("skipped").GetBoolean());
            Assert.Equal("awaiting_partner", sdoc.RootElement.GetProperty("error").GetString());
            var explain = sdoc.RootElement.GetProperty("explain");
            Assert.Equal("ignite.continuity", explain.GetProperty("source").GetString());
            Assert.Equal("awaiting_partner", explain.GetProperty("reason").GetString());
            Assert.Equal("cdp_ignite op=resume", explain.GetProperty("next_step").GetString());
            var resume = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("resume") });
            using var rdoc = JsonDocument.Parse(JsonSerializer.Serialize(resume));
            Assert.True(rdoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(rdoc.RootElement.GetProperty("removed").GetInt32() >= 1);
            Assert.DoesNotContain(IdeIgniteArmHost.Snapshot(), a => a.Id == id);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("disarm"), ["id"] = JsonSerializer.SerializeToElement(id) });
        }
    }

    [Theory]
    [InlineData("tool-wake-abc", true)]
    [InlineData("arm-20260729-xx", false)]
    [InlineData(null, false)]
    public void IsToolWakeArmId_prefix(string? id, bool expect) => Assert.Equal(expect, IdeIgniteArmHost.IsToolWakeArmId(id));
    [Theory]
    [InlineData("tool-wake-abc", true)]
    [InlineData("remount-wake-20260730-xx", true)]
    [InlineData("oom-wake-20260731-xx", true)]
    [InlineData("hild-escalate-away", true)]
    [InlineData("hild-escalate-20260801-xx", true)]
    [InlineData("hild-away", true)]
    [InlineData("hild-away-20260801-xx", true)]
    [InlineData("arm-20260729-xx", false)]
    [InlineData(null, false)]
    public void IsSystemWakeArmId_prefix(string? id, bool expect) => Assert.Equal(expect, IdeIgniteArmHost.IsSystemWakeArmId(id));
    [Fact]
    public void TryScheduleHildEscalateWake_arms_escalate_charge()
    {
        var scheduled = IdeIgniteArmHost.TryScheduleHildEscalateWake();
        Assert.NotNull(scheduled);
        var json = JsonSerializer.Serialize(scheduled);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(IdeIgniteArmHost.HildEscalateChargeMode, doc.RootElement.GetProperty("charge_mode").GetString());
        Assert.Equal(IdeIgniteArmHost.HildEscalateReason, doc.RootElement.GetProperty("reason").GetString());
        var id = doc.RootElement.GetProperty("id").GetString();
        Assert.Equal(IdeIgniteArmHost.HildEscalateArmId, id);
        IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) { ["id"] = JsonSerializer.SerializeToElement(id!) });
    }

    [Fact]
    public void TryScheduleHildEscalateWake_replaces_prior_stable_id()
    {
        var first = IdeIgniteArmHost.TryScheduleHildEscalateWake();
        var second = IdeIgniteArmHost.TryScheduleHildEscalateWake();
        Assert.NotNull(first);
        Assert.NotNull(second);
        using var d1 = JsonDocument.Parse(JsonSerializer.Serialize(first));
        using var d2 = JsonDocument.Parse(JsonSerializer.Serialize(second));
        Assert.Equal(IdeIgniteArmHost.HildEscalateArmId, d1.RootElement.GetProperty("id").GetString());
        Assert.Equal(IdeIgniteArmHost.HildEscalateArmId, d2.RootElement.GetProperty("id").GetString());
        Assert.True(IdeIgniteArmHost.TryMutateForTests(IdeIgniteArmHost.HildEscalateArmId, _ =>
        {
        }));
        IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) { ["id"] = JsonSerializer.SerializeToElement(IdeIgniteArmHost.HildEscalateArmId) });
    }

    [Theory]
    [InlineData("build_finished", true)]
    [InlineData("build", true)]
    [InlineData("test_finished", true)]
    [InlineData("shell_finished", true)]
    [InlineData("timer", false)]
    [InlineData("manual", false)]
    public void IsEventTriggeredArm_events(string ev, bool expect) => Assert.Equal(expect, IdeIgniteArmHost.IsEventTriggeredArm(ev));
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