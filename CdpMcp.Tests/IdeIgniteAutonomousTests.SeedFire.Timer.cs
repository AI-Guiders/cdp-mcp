using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;
public partial class IdeIgniteAutonomousTests
{
    [Fact]
    public void Autonomous_last_once_insurance_clamps_long_timer_to_3m()
    {
        var clamped = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(45), lastOnce: true, autonomous: true, force: false, out var note);
        Assert.Equal(TimeSpan.FromMinutes(3), clamped);
        Assert.Equal("3m(clamped)", note);
        var kept = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(45), lastOnce: true, autonomous: true, force: true, out var forceNote);
        Assert.Equal(TimeSpan.FromMinutes(45), kept);
        Assert.Null(forceNote);
        var partner = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(45), lastOnce: true, autonomous: false, force: false, out var partnerNote);
        Assert.Equal(TimeSpan.FromMinutes(45), partner);
        Assert.Null(partnerNote);
        var away = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(3), lastOnce: true, autonomous: true, force: false, out var awayNote, partnerAway: true);
        Assert.Equal(TimeSpan.FromSeconds(3), away);
        Assert.Equal("3s(hild_away)", awayNote);
        var leaf = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(45), lastOnce: true, autonomous: true, force: false, out var leafNote, partnerAway: false, leafFlying: true);
        Assert.Equal(TimeSpan.FromSeconds(3), leaf);
        Assert.Equal("3s(leaf_started)", leafNote);
        var inventHold = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(15), lastOnce: true, autonomous: true, force: false, out var inventHoldNote, partnerAway: false, leafFlying: true, inventOnlyHold: true);
        Assert.Equal(TimeSpan.FromMinutes(15), inventHold);
        Assert.Null(inventHoldNote);
        var inventHoldLong = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(45), lastOnce: true, autonomous: true, force: false, out var inventHoldLongNote, partnerAway: false, leafFlying: true, inventOnlyHold: true);
        Assert.Equal(TimeSpan.FromMinutes(15), inventHoldLong);
        Assert.Equal("15m(invent_only_hold)", inventHoldLongNote);
        // Partner-away wins over leaf-fly for the note tag — except invent-only Hold (≤15m).
        var awayWins = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(45), lastOnce: true, autonomous: true, force: false, out var awayWinsNote, partnerAway: true, leafFlying: true);
        Assert.Equal(TimeSpan.FromSeconds(3), awayWins);
        Assert.Equal("3s(hild_away)", awayWinsNote);
        var inventHoldAway = IdeIgniteArmHost.ClampAutonomousLastOnceInsurance(TimeSpan.FromMinutes(15), lastOnce: true, autonomous: true, force: false, out var inventHoldAwayNote, partnerAway: true, leafFlying: true, inventOnlyHold: true);
        Assert.Equal(TimeSpan.FromMinutes(15), inventHoldAway);
        Assert.Null(inventHoldAwayNote);
    }

    [Fact]
    public void Hild_away_pull_forward_computes_3s_due_for_long_last_once_work_timer()
    {
        var now = DateTimeOffset.Parse("2026-08-02T20:00:00Z");
        Assert.True(IdeIgniteArmHost.TryComputeHildAwayPullForwardDue(dueUtc: now.AddMinutes(45), lastOnce: true, isAutonomyMeans: false, status: "armed", eventKind: "timer", now: now, out var newDue, out var note));
        Assert.Equal(now.AddSeconds(3), newDue);
        Assert.Equal("3s(hild_pull)", note);
        Assert.False(IdeIgniteArmHost.TryComputeHildAwayPullForwardDue(dueUtc: now.AddSeconds(2), lastOnce: true, isAutonomyMeans: false, status: "armed", eventKind: "timer", now: now, out _, out _));
        Assert.False(IdeIgniteArmHost.TryComputeHildAwayPullForwardDue(dueUtc: now.AddMinutes(45), lastOnce: true, isAutonomyMeans: true, status: "armed", eventKind: "timer", now: now, out _, out _));
    }

    [Fact]
    public void Leaf_fly_pull_forward_computes_3s_due_with_leaf_pull_note()
    {
        var now = DateTimeOffset.Parse("2026-08-02T20:00:00Z");
        Assert.True(IdeIgniteArmHost.TryComputeLeafFlyPullForwardDue(dueUtc: now.AddMinutes(45), lastOnce: true, isAutonomyMeans: false, status: "armed", eventKind: "timer", now: now, out var newDue, out var note));
        Assert.Equal(now.AddSeconds(3), newDue);
        Assert.Equal("3s(leaf_pull)", note);
    }

    [Fact]
    public void IsInventOnlyHoldTask_matches_hold_invent_only_title()
    {
        Assert.True(IdeIgniteArmHost.IsInventOnlyHoldTask("Hold Citizen Done stable to 15.08 — invent only on real product gap"));
        Assert.True(IdeIgniteArmHost.IsInventOnlyHoldTask("Hold invent-only to 15.08 — SoftFL REJECT; dig only on lived product residual"));
        Assert.False(IdeIgniteArmHost.IsInventOnlyHoldTask("Ship cabin detach survival"));
        Assert.False(IdeIgniteArmHost.IsInventOnlyHoldTask(null));
    }
}