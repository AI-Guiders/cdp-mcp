#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class IdeHildDetectorTests
{
    static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    [Fact]
    public void Voice_empty_for_5s_edges_human_away_once()
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

        var r4 = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(10), Idle));
        Assert.Equal(IdeHildDetector.Status.HumanAway, r4.Status);
        Assert.False(r4.EdgeHumanAway);
    }

    [Fact]
    public void Composer_text_resets_watch()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(4), Idle));

        var present = d.Tick(new IdeHildDetector.Sample("send", "hello", t0.AddSeconds(4.5), Idle));
        Assert.Equal(IdeHildDetector.Status.HumanPresent, present.Status);
        Assert.False(present.EdgeHumanAway);

        // Cleared back to Voice — new 5s spell.
        var watch = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(5), Idle));
        Assert.Equal(IdeHildDetector.Status.Watching, watch.Status);

        var early = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(9), Idle));
        Assert.False(early.EdgeHumanAway);

        var edge = d.Tick(new IdeHildDetector.Sample("voice", "", t0.AddSeconds(10), Idle));
        Assert.True(edge.EdgeHumanAway);
    }

    [Fact]
    public void Stop_resets_spell()
    {
        var d = new IdeHildDetector();
        var t0 = DateTimeOffset.Parse("2026-07-31T06:00:00Z");

        d.Tick(new IdeHildDetector.Sample("voice", "", t0, Idle));
        var stop = d.Tick(new IdeHildDetector.Sample("stop", "", t0.AddSeconds(2), Idle));
        Assert.Equal(IdeHildDetector.Status.Idle, stop.Status);

        // After agent finishes → Voice again needs full idle.
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
