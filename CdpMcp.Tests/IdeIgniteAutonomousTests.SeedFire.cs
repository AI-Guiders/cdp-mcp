using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public partial class IdeIgniteAutonomousTests
{
    [Fact]
    public void AutonomousSeed_fire_with_incomplete_leaf_redirects_to_leaf_wake()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindAutonomous(false);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        IdeIgniteArmHost.BindAutonomous(true);

        _ = IdeIgniteArmHost.AutonomousContinue("task_done_exhausted");
        IdeIgniteArmHost.BindIncompleteLeafTitleProbe(() => "Ship Cursor-dep tooth");

        Assert.True(
            IdeIgniteArmHost.TrySuppressLiveAutonomousSeedBeforeDelivery(),
            "seed fire must suppress when incomplete leaf already landed");

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var arms = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(IdeIgniteArmHost.AutonomousSeedArmId, arms);
        Assert.Contains(IdeIgniteArmHost.LeafWakeArmId, arms);
    }

    [Fact]
    public void AutonomousSeed_fire_empty_board_does_not_suppress()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindAutonomous(false);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        IdeIgniteArmHost.BindAutonomous(true);

        _ = IdeIgniteArmHost.AutonomousContinue("task_done_exhausted");
        IdeIgniteArmHost.BindIncompleteLeafTitleProbe(() => null);

        Assert.False(IdeIgniteArmHost.TrySuppressLiveAutonomousSeedBeforeDelivery());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var arms = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(IdeIgniteArmHost.AutonomousSeedArmId, arms);
    }

    [Fact]
    public void LastOnceArm_tips_under_autonomous_forbid_park_on_timer()
    {
        Assert.Contains("do not park", IdeIgniteArmHost.LastOnceArmNextStep(autonomous: true), StringComparison.Ordinal);
        Assert.Contains("NOT permission to idle", IdeIgniteArmHost.LastOnceArmHint(autonomous: true), StringComparison.Ordinal);
        Assert.Equal("end turn", IdeIgniteArmHost.LastOnceArmNextStep(autonomous: false));
        Assert.Contains("awaiting latch", IdeIgniteArmHost.LastOnceArmHint(autonomous: false), StringComparison.Ordinal);
    }

    [Fact]
    public void ArmForLeafHint_under_autonomous_does_not_teach_end_turn_park()
    {
        var auto = IdeIgniteArmHost.ArmForLeafHint(autonomous: true);
        Assert.Contains("Keep flying", auto, StringComparison.Ordinal);
        Assert.Contains("not a license to park", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("End turn", auto, StringComparison.Ordinal);

        var partner = IdeIgniteArmHost.ArmForLeafHint(autonomous: false);
        Assert.Contains("End turn", partner, StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuityArmedNextStep_under_autonomous_forbids_wait_for_event_park()
    {
        var auto = IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: true);
        Assert.Contains("keep flying", auto, StringComparison.Ordinal);
        Assert.Contains("do not park", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("wait for event", auto, StringComparison.Ordinal);

        Assert.Equal("wait for event", IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: false));
    }

    [Fact]
    public void PressureTips_under_autonomous_forbid_end_turn_park()
    {
        var check = IdePressureChannel.AutoIgnitionChecklistLine(autonomous: true);
        Assert.Contains("keep flying", check, StringComparison.Ordinal);
        Assert.Contains("insurance", check, StringComparison.Ordinal);
        Assert.DoesNotContain("end turn", check, StringComparison.OrdinalIgnoreCase);

        var scene = IdePressureChannel.SceneArmedHint(autonomous: true);
        Assert.Contains("not a nap", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("before end turn", scene, StringComparison.OrdinalIgnoreCase);

        var stash = IdePressureChannel.StashHint(autonomous: true);
        Assert.Contains("do not park", stash, StringComparison.Ordinal);
        Assert.DoesNotContain("ending turn", stash, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("before end turn", IdePressureChannel.AutoIgnitionChecklistLine(autonomous: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before end turn", IdePressureChannel.SceneArmedHint(autonomous: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ending turn", IdePressureChannel.StashHint(autonomous: false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContinuityArmedNextStep_used_for_non_last_once_explain_under_autonomous()
    {
        // ArmPath event arms: autonomous must not teach wait-for-event / end-turn park.
        Assert.DoesNotContain("wait for event", IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: true), StringComparison.Ordinal);
        Assert.Contains("keep flying", IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: true), StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalComposerCharge_does_not_teach_rearm_when_idle()
    {
        Assert.Contains("timer ≠ idle license", IdeIgniteChannel.CanonicalComposerCharge, StringComparison.Ordinal);
        Assert.DoesNotContain("re-arm when idle", IdeIgniteChannel.CanonicalComposerCharge, StringComparison.Ordinal);
    }

    [Fact]
    public void Autonomous_last_once_insurance_clamps_long_timer_to_3m()
    {
        var clamped = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(
            TimeSpan.FromMinutes(45),
            lastOnce: true,
            autonomous: true,
            force: false,
            out var note);
        Assert.Equal(TimeSpan.FromMinutes(3), clamped);
        Assert.Equal("3m(clamped)", note);

        var kept = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(
            TimeSpan.FromMinutes(45),
            lastOnce: true,
            autonomous: true,
            force: true,
            out var forceNote);
        Assert.Equal(TimeSpan.FromMinutes(45), kept);
        Assert.Null(forceNote);

        var partner = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(
            TimeSpan.FromMinutes(45),
            lastOnce: true,
            autonomous: false,
            force: false,
            out var partnerNote);
        Assert.Equal(TimeSpan.FromMinutes(45), partner);
        Assert.Null(partnerNote);

        var away = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(
            TimeSpan.FromMinutes(3),
            lastOnce: true,
            autonomous: true,
            force: false,
            out var awayNote,
            partnerAway: true);
        Assert.Equal(TimeSpan.FromSeconds(3), away);
        Assert.Equal("3s(hild_away)", awayNote);
    }

    [Fact]
    public void Hild_away_pull_forward_computes_3s_due_for_long_last_once_work_timer()
    {
        var now = DateTimeOffset.Parse("2026-08-02T20:00:00Z");
        Assert.True(IdeIgniteArmHost.TryComputeHildAwayPullForwardDue(
            dueUtc: now.AddMinutes(45),
            lastOnce: true,
            isAutonomyMeans: false,
            status: "armed",
            eventKind: "timer",
            now: now,
            out var newDue,
            out var note));
        Assert.Equal(now.AddSeconds(3), newDue);
        Assert.Equal("3s(hild_pull)", note);

        Assert.False(IdeIgniteArmHost.TryComputeHildAwayPullForwardDue(
            dueUtc: now.AddSeconds(2),
            lastOnce: true,
            isAutonomyMeans: false,
            status: "armed",
            eventKind: "timer",
            now: now,
            out _,
            out _));

        Assert.False(IdeIgniteArmHost.TryComputeHildAwayPullForwardDue(
            dueUtc: now.AddMinutes(45),
            lastOnce: true,
            isAutonomyMeans: true,
            status: "armed",
            eventKind: "timer",
            now: now,
            out _,
            out _));
    }
}
