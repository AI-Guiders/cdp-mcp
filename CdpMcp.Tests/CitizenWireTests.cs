#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class CitizenWireTests : IDisposable
{
    public CitizenWireTests()
    {
        CitizenWire.Inject = false;
    }

    public void Dispose()
    {
        CitizenWire.Inject = false;
    }

    [Fact]
    public void PackDesk_round_trips_through_parser()
    {
        var pulse = new CitizenWire.DeskPulse(
            Board: "P:plan · peel7 | F:editor · 0 buf | M:shell",
            Sa: "clear · explore/code",
            Peer: "ok · gen=1 · mcp=live · compact=no",
            Next: "plan | editor",
            Tm: "feature=citizen wire · focus=packer",
            Cost: "A");

        var packed = CitizenWire.PackDesk(pulse);
        var msgs = CitizenWireParser.Parse(packed);

        Assert.Single(msgs);
        var m = msgs[0];
        Assert.Equal(CitizenWireParser.Kind.Frame, m.Kind);
        Assert.Equal("desk", m.Type);
        Assert.Equal("v0", m.Version);
        Assert.Equal(pulse.Board, m.Fields["board"]);
        Assert.Equal(pulse.Sa, m.Fields["sa"]);
        Assert.Equal(pulse.Peer, m.Fields["peer"]);
        Assert.Equal(pulse.Next, m.Fields["next"]);
        Assert.Equal(pulse.Tm, m.Fields["tm"]);
        Assert.Equal("A", m.Fields["cost"]);
    }

    [Fact]
    public void PrependAfferent_off_by_default()
    {
        var packed = CitizenWire.PackDesk(new CitizenWire.DeskPulse("b", "sa"));
        var msgs = new[] { "user: hello" };
        var outMsgs = CitizenWire.PrependAfferent(msgs, packed);
        Assert.Same(msgs, outMsgs);
    }

    [Fact]
    public void PrependAfferent_inject_on_puts_pulse_first()
    {
        CitizenWire.Inject = true;
        var packed = CitizenWire.PackDesk(new CitizenWire.DeskPulse(
            Board: "P:plan",
            Sa: "clear",
            Peer: "ok · gen=3"));
        var msgs = new[] { "user: hello" };
        var outMsgs = CitizenWire.PrependAfferent(msgs, packed);

        Assert.Equal(2, outMsgs.Count);
        Assert.StartsWith("@frame desk v0", outMsgs[0], StringComparison.Ordinal);
        Assert.Equal("user: hello", outMsgs[1]);

        var parsed = CitizenWireParser.Parse(outMsgs[0]);
        Assert.Single(parsed);
        Assert.Contains("gen=3", parsed[0].Fields["peer"]);
    }
}
