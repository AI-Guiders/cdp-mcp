#nullable enable
using CdpMcp.Cockpit.DataBus;
using Xunit;

namespace CdpMcp.Tests;

public sealed class InMemoryDataBusTests
{
    sealed record Ping(string Id);

    [Fact]
    public void Publish_delivers_to_subscriber()
    {
        using var bus = new InMemoryDataBus();
        string? got = null;
        using var _ = bus.Subscribe<Ping>(p => got = p.Id);
        bus.Publish(new Ping("a"));
        Assert.Equal("a", got);
    }

    [Fact]
    public void Dispose_subscription_stops_delivery()
    {
        using var bus = new InMemoryDataBus();
        var n = 0;
        var sub = bus.Subscribe<Ping>(_ => n++);
        sub.Dispose();
        bus.Publish(new Ping("x"));
        Assert.Equal(0, n);
    }
}
