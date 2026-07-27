#nullable enable
using System.Threading.Channels;

namespace CdpMcp.Cockpit.Transport;

/// <summary>Typed desk ingress event (ADR 0094) — MCP/cockpit request into the wire.</summary>
public readonly record struct DeskIngressEvent(
    string Source,
    string? CmdLine,
    string? GoVerb,
    DateTimeOffset Utc);

/// <summary>
/// Process-local ingestion bus: bounded <see cref="Channel{T}"/> with Wait backpressure (CIDE BuildLogIngestion spirit).
/// </summary>
public sealed class DeskIngestionBus : IDisposable
{
    public const int DefaultCapacity = 64;

    readonly Channel<DeskIngressEvent> _channel;
    long _published;
    long _dropped;

    public DeskIngestionBus(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<DeskIngressEvent>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public ChannelReader<DeskIngressEvent> Reader => _channel.Reader;

    public long Published => Interlocked.Read(ref _published);
    public long Dropped => Interlocked.Read(ref _dropped);

    public bool TryPublish(DeskIngressEvent evt)
    {
        if (_channel.Writer.TryWrite(evt))
        {
            Interlocked.Increment(ref _published);
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public object Pulse() => new
    {
        seam = "transport",
        adr = "0094",
        real = true,
        queue = "channel",
        capacity = DefaultCapacity,
        published = Published,
        dropped = Dropped,
        count = _channel.Reader.Count
    };

    public void Dispose() => _channel.Writer.TryComplete();
}

/// <summary>Process host for desk ingestion (one bus per MCP process).</summary>
public static class DeskIngestionHost
{
    static readonly Lazy<DeskIngestionBus> Bus = new(() => new DeskIngestionBus());
    public static DeskIngestionBus Current => Bus.Value;
}
