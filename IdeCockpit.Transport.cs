#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Transport peel — desk ingress of cockpit requests (CIDE ADR 0094 spirit, no Avalonia).
/// Delivery into CCU (<c>BuildAsync</c>), not cabin Channel/CDS routing.
/// Peel: typed envelope; not full System.Threading.Channel bus yet.
/// </summary>
internal static partial class IdeCockpit
{
    public readonly record struct TransportEnvelope(
        IReadOnlyDictionary<string, JsonElement> Args,
        string? CmdLine,
        string Source,
        DateTimeOffset IngestedUtc);

    /// <summary>Ingest MCP/cockpit args before CCU — one predictable entry (ADR 0094).</summary>
    public static TransportEnvelope IngestCockpitRequest(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var cmd = OptString(args, "cmd") ?? OptString(args, "line") ?? OptString(args, "repl")
            ?? OptString(args, "ccl") ?? OptString(args, "ccc");
        var source = OptString(args, "transport_source") ?? "mcp_cockpit";
        return new TransportEnvelope(args, cmd, source, DateTimeOffset.UtcNow);
    }

    /// <summary>Pulse for arch/debug — queue is sync peel (no bounded channel yet).</summary>
    public static object TransportPulse(TransportEnvelope? last = null) => new
    {
        seam = "transport",
        adr = "0094",
        peel = true,
        queue = "sync",
        last_source = last?.Source,
        last_cmd = last?.CmdLine is { Length: > 0 },
        ingested_utc = last?.IngestedUtc
    };
}
