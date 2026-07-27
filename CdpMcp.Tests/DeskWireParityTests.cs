#nullable enable
using CdpMcp.Cockpit.Composition;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.DataBus;
using CdpMcp.Cockpit.Instrument;
using CdpMcp.Cockpit.Transport;
using Xunit;

namespace CdpMcp.Tests;

public sealed class DeskWireParityTests
{
    [Fact]
    public void DeskIngestionBus_publishes_on_channel()
    {
        using var bus = new DeskIngestionBus(8);
        Assert.True(bus.TryPublish(new DeskIngressEvent("t", "cmd", "sa", DateTimeOffset.UtcNow)));
        Assert.Equal(1, bus.Published);
        Assert.True(bus.Reader.TryRead(out var evt));
        Assert.Equal("t", evt.Source);
    }

    [Fact]
    public void AttentionRoutingUnit_forces_nav_desk_detail()
    {
        var unit = new AttentionRoutingUnit();
        var snap = unit.Compute(new AttentionRoutingUnit.Input("sys", "nav", true, null));
        Assert.Equal("nav", snap.Mfd);
        Assert.Null(snap.GoVerb);
        Assert.True(snap.DeskDetailNavForced);
    }

    [Fact]
    public void DeskInstrumentMountRegistry_syncs_seats()
    {
        var reg = new DeskInstrumentMountRegistry();
        reg.SyncFromSeats(new Dictionary<string, string?> { ["p"] = "plan", ["forward"] = "editor_scene", ["m"] = null },
            ["p", "forward", "m"]);
        var deck = reg.Describe();
        Assert.Equal(2, deck.OrderedInstrumentIds.Count);
        Assert.Contains("p:plan", deck.OrderedInstrumentIds);
    }

    [Fact]
    public void DeskDataBusHost_publishes_surface_built()
    {
        DeskSurfaceBuiltEvent? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<DeskSurfaceBuiltEvent>(e => got = e);
        DeskDataBusHost.Current.Publish(new DeskSurfaceBuiltEvent("seats", 3, null, DateTimeOffset.UtcNow));
        Assert.NotNull(got);
        Assert.Equal("seats", got!.Value.Mode);
        Assert.Equal(3, got.Value.SeatCount);
    }

    [Fact]
    public void DeskDetailUnit_focus_forces_nav()
    {
        var unit = new DeskDetailUnit();
        var snap = unit.Compute(new DeskDetailUnit.Input("slim", "buf"));
        Assert.Equal("nav", snap.DeskDetail);
        Assert.True(snap.WantNav);
    }

    [Fact]
    public void SeatsSurfaceCompositor_publishes_and_sets_desk_detail()
    {
        DeskSurfaceBuiltEvent? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<DeskSurfaceBuiltEvent>(e => got = e);
        var comp = new SeatsSurfaceCompositor();
        var decision = new DeskDetailUnit.Snapshot("nav", true);
        var dict = comp.Compose(
            new SeatsSurfaceScene(
                "cockpit/v1.20", "sa", new { }, new { }, new { },
                null, null, null, new { }, null,
                null, null, ["p"], ["agent"], null,
                Array.Empty<object>(), ["sa"]),
            new SeatsSurfacePayload(2),
            decision);
        Assert.Equal("nav", dict["desk_detail"]);
        Assert.NotNull(dict["loci"]);
        Assert.NotNull(got);
        Assert.Equal(2, got!.Value.SeatCount);
    }
}
