#nullable enable
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CdpMcp;

internal static partial class IdeElicit
{
    static string Peek(McpServer server, (bool Supported, object? Raw) caps) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "peek",
            client = ClientPulse(server),
            elicitation = caps,
            hint = caps.Supported
                ? "Client advertises elicitation — try op=ask."
                : "Client did not advertise elicitation — path 2 blocked at host."
        }, Pretty);

    static object ClientPulse(McpServer server) => new
    {
        name = server.ClientInfo?.Name,
        version = server.ClientInfo?.Version,
        protocol = server.NegotiatedProtocolVersion
    };

    static (bool Supported, object? Raw) CapsPulse(McpServer server)
    {
        var e = server.ClientCapabilities?.Elicitation;
        if (e is null)
            return (false, null);
        return (true, new { form = e.Form is not null, url = e.Url is not null });
    }
}
