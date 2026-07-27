#nullable enable
using CdpMcp.Cockpit.DataBus;

namespace CdpMcp.Cockpit.DataBus;

/// <summary>Process-local IDE DataBus host (ADR 0099) for desk domain events.</summary>
public static class DeskDataBusHost
{
    static readonly Lazy<InMemoryDataBus> Bus = new(() => new InMemoryDataBus());
    public static IDataBus Current => Bus.Value;
}

/// <summary>Published after a cockpit BuildAsync completes a seats surface.</summary>
public readonly record struct DeskSurfaceBuiltEvent(
    string Mode,
    int SeatCount,
    string? Go,
    DateTimeOffset Utc);
