#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CdpMcp;

/// <summary>
/// Spike: MCP elicitation/create → host UI (path 2).
/// Dogfood whether Cursor advertises elicitation and shows a form.
/// </summary>
internal static class IdeElicit
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
        {
            return JsonSerializer.Serialize(new
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
        }

        var message = Opt(args, "message") ?? Opt(args, "ask")
            ?? "CDP elicit spike (path 2): подтверди?";

        try
        {
            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["choice"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                        {
                            Description = "Да / Нет / Обсудить",
                            OneOf =
                            [
                                new() { Const = "yes", Title = "Да" },
                                new() { Const = "no", Title = "Нет" },
                                new() { Const = "discuss", Title = "Обсудить" },
                            ],
                            Default = "discuss"
                        }
                    },
                    Required = ["choice"]
                }
            }, cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "ask",
                action = result.Action,
                accepted = result.IsAccepted,
                content = result.Content,
                elicitation = caps,
                hint = "Host answered elicitation/create."
            }, Pretty);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "ask",
                error = "client_no_elicitation",
                detail = ex.Message,
                elicitation = caps,
                client = ClientPulse(server),
                hint = "Cursor likely lacks elicitation capability — path 2 needs host support."
            }, Pretty);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "ask",
                error = "elicit_failed",
                detail = ex.Message,
                type = ex.GetType().FullName,
                elicitation = caps,
                client = ClientPulse(server)
            }, Pretty);
        }
    }

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

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
