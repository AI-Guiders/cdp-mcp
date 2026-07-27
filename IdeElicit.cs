#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CdpMcp;

/// <summary>
/// Spike: MCP elicitation/create → host UI (path 2).
/// Dogfood whether Cursor advertises elicitation and shows a form.
/// </summary>
internal static partial class IdeElicit
{
    public const string Schema = "elicit/v0";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<string> RunAsync(
        McpServer? server,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "elicit",
                error = "no_server",
                hint = "McpServer not bound yet."
            }, Pretty);
        }

        var op = (Opt(args, "op") ?? "ask").Trim().ToLowerInvariant();
        var caps = CapsPulse(server);

        if (op is "peek" or "caps" or "status")
            return Peek(server, caps);

        var message = Opt(args, "message") ?? Opt(args, "ask")
            ?? "CDP elicit spike (path 2): подтверди?";

        try
        {
            return await AskAsync(server, message, caps, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return ClientNoElicitation(server, caps, ex.Message);
        }
        catch (Exception ex)
        {
            return ElicitFailed(server, caps, ex);
        }
    }
}
