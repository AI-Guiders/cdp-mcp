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

    [Fact]
    public void FromDeskBoard_normalizes_cockpit_seat_rows()
    {
        var pulse = CitizenWire.FromDeskBoard(
            [
                "P  plan       · citizen wire peels #8",
                "F  editor     · CitizenWire.cs",
                "M  shell      · shell",
            ],
            sa: "clear · explore/code",
            peer: "ok · gen=1",
            next: "plan | editor",
            tm: "focus=#8 binder");

        Assert.Equal("P:plan · citizen wire peels #8 | F:editor · CitizenWire.cs | M:shell · shell", pulse.Board);
        Assert.Equal("clear · explore/code", pulse.Sa);
        Assert.Equal("ok · gen=1", pulse.Peer);
        Assert.Equal("A", pulse.Cost);
    }

    [Fact]
    public void PackFromDeskBoard_round_trips_parser()
    {
        var packed = CitizenWire.PackFromDeskBoard(
            ["| P:plan | F:editor · 0 buf | M:shell |"],
            sa: "sa · clear",
            peer: "ok · gen=2 · mcp=live");

        var msgs = CitizenWireParser.Parse(packed);
        Assert.Single(msgs);
        Assert.Equal(CitizenWireParser.Kind.Frame, msgs[0].Kind);
        Assert.Contains("P:plan", msgs[0].Fields["board"]);
        Assert.Contains("F:editor · 0 buf", msgs[0].Fields["board"]);
        Assert.Contains("M:shell", msgs[0].Fields["board"]);
        Assert.Equal("sa · clear", msgs[0].Fields["sa"]);
        Assert.Contains("gen=2", msgs[0].Fields["peer"]);
    }

    [Fact]
    public void FromDeskBoard_empty_board_is_safe()
    {
        var pulse = CitizenWire.FromDeskBoard(null);
        Assert.Equal("(empty)", pulse.Board);
        Assert.Equal("clear", pulse.Sa);
    }

}
