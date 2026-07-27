#nullable enable
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
}
