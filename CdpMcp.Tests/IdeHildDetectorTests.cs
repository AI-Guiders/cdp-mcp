#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class IdeHildDetectorTests
{
    /// <summary>Short idle injected into Sample — FSM clock tests, not DefaultIdle.</summary>
    static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    [Fact]
    public void DefaultIdle_is_30s()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), IdeHildDetector.DefaultIdle);
    }

    [Fact]
    public void Voice_empty_for_idle_threshold_edges_human_away_once()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        var r1 = d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        Assert.Equal(IdeHildDetector.Status.Watching, r1.Status);
        Assert.False(r1.EdgeHumanAway);

        var r2 = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(4), Idle));
        Assert.Equal(IdeHildDetector.Status.Watching, r2.Status);
        Assert.False(r2.EdgeHumanAway);

        var r3 = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(5), Idle));
        Assert.Equal(IdeHildDetector.Status.HumanAway, r3.Status);
        Assert.True(r3.EdgeHumanAway);
        Assert.True(d.AwayLatched);

        var r4 = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(10), Idle));
        Assert.Equal(IdeHildDetector.Status.HumanAway, r4.Status);
        Assert.False(r4.EdgeHumanAway);
    }

    [Fact]
    public void Composer_text_resets_latch_for_next_leave()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        Assert.True(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(5), Idle)).EdgeHumanAway);

        var present = d.Tick(new IdeHildDetector.Sample("send", "hello", t0.AddSeconds(6), Idle));
        Assert.Equal(IdeHildDetector.Status.HumanPresent, present.Status);
        Assert.False(d.AwayLatched);

        d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(7), Idle));
        Assert.True(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(12), Idle)).EdgeHumanAway);
    }

    [Fact]
    public void Stop_after_edge_does_not_refire()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        Assert.True(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(5), Idle)).EdgeHumanAway);

        d.Tick(new IdeHildDetector.Sample("stop", "", t0.AddSeconds(6), Idle));
        d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(7), Idle));
        Assert.False(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(20), Idle)).EdgeHumanAway);
        Assert.True(d.AwayLatched);
    }

    [Fact]
    public void AutoIgnition_charge_text_does_not_clear_latch()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        Assert.True(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(5), Idle)).EdgeHumanAway);

        var charge = IdeIgniteChannel.ComposeArmFireCharge();
        var inject = d.Tick(new IdeHildDetector.Sample("send", charge, t0.AddSeconds(6), Idle));
        Assert.Equal(IdeHildDetector.Status.HumanAway, inject.Status);
        Assert.True(d.AwayLatched);
        Assert.False(inject.EdgeHumanAway);

        d.Tick(new IdeHildDetector.Sample("stop", "", t0.AddSeconds(7), Idle));
        d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(8), Idle));
        Assert.False(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(20), Idle)).EdgeHumanAway);
        Assert.True(d.AwayLatched);
    }

    [Fact]
    public void Stop_before_edge_allows_later_edge()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        d.Tick(new IdeHildDetector.Sample("stop", "", t0.AddSeconds(2), Idle));
        d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(3), Idle));
        Assert.False(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(7), Idle)).EdgeHumanAway);
        Assert.True(d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(8), Idle)).EdgeHumanAway);
    }

    [Fact]
    public void Aria_labels_normalize()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");
        var r = d.Tick(new IdeHildDetector.Sample("Voice input", null, t0, Idle));
        Assert.Equal(IdeHildDetector.Status.Watching, r.Status);
    }
}
