#nullable enable
using System.Text.Json;
using CdpMcp.Cockpit.Transport;

namespace CdpMcp;

/// <summary>
/// Transport peel — desk ingress via <see cref="DeskIngestionBus"/> (ADR 0094, Channel&lt;T&gt;).
/// </summary>
internal static partial class IdeCockpit
{
    public readonly record struct TransportEnvelope(
        IReadOnlyDictionary<string, JsonElement> Args,
        string? CmdLine,
        string Source,
        DateTimeOffset IngestedUtc);

    /// <summary>Ingest MCP/cockpit args before CCU — publish onto Channel&lt;T&gt; bus.</summary>
    public static TransportEnvelope IngestCockpitRequest(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var cmd = OptString(args, "cmd") ?? OptString(args, "line") ?? OptString(args, "repl")
            ?? OptString(args, "ccl") ?? OptString(args, "ccc");
        var source = OptString(args, "transport_source") ?? "mcp_cockpit";
        var go = OptString(args, "go") ?? OptString(args, "do");
        var utc = DateTimeOffset.UtcNow;
        DeskIngestionHost.Current.TryPublish(new DeskIngressEvent(source, cmd, go, utc));
        return new TransportEnvelope(args, cmd, source, utc);
    }

    /// <summary>Pulse — real Channel&lt;T&gt; bus counters.</summary>
    public static object TransportPulse(TransportEnvelope? last = null)
    {
        var bus = DeskIngestionHost.Current.Pulse();
        return new
        {
            seam = "transport",
            adr = "0094",
            peel = true,
            real = true,
            last_source = last?.Source,
            last_cmd = last?.CmdLine is { Length: > 0 },
            ingested_utc = last?.IngestedUtc,
            bus
        };
    }
}
