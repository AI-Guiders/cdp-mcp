#nullable enable
using System.Threading.Channels;

namespace CdpMcp.Cockpit.Transport;

/// <summary>
/// Process-local desk ingress on <see cref="BoundedIngressBus{IngressEvent}"/> (ADR 0094).
/// </summary>
public sealed class DeskIngestionBus : IDisposable
{
    readonly BoundedIngressBus<IngressEvent> _bus;

    public DeskIngestionBus(int capacity = BoundedIngressBus<IngressEvent>.DefaultCapacity)
    {
        _bus = new BoundedIngressBus<IngressEvent>(capacity);
    }

    public ChannelReader<IngressEvent> Reader => _bus.Reader;

    public long Published => _bus.Published;

    public long Dropped => _bus.Dropped;

    public bool TryPublish(IngressEvent evt) => _bus.TryPublish(evt);

    public object Pulse() => new
    {
        seam = "transport",
        adr = "0094",
        real = true,
        queue = "channel",
        capacity = BoundedIngressBus<IngressEvent>.DefaultCapacity,
        published = Published,
        dropped = Dropped,
        count = _bus.Reader.Count
    };

    public void Dispose() => _bus.Dispose();
}

/// <summary>Process host for desk ingestion (one bus per MCP process).</summary>
public static class DeskIngestionHost
{
    static readonly Lazy<DeskIngestionBus> Bus = new(() => new DeskIngestionBus());
    public static DeskIngestionBus Current => Bus.Value;
}
