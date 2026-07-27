#nullable enable
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CdpMcp;

internal static partial class IdeElicit
{
    static async Task<string> AskAsync(
        McpServer server,
        string message,
        (bool Supported, object? Raw) caps,
        CancellationToken cancellationToken)
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

    static string ClientNoElicitation(
        McpServer server,
        (bool Supported, object? Raw) caps,
        string detail) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = false,
            op = "ask",
            error = "client_no_elicitation",
            detail,
            elicitation = caps,
            client = ClientPulse(server),
            hint = "Cursor likely lacks elicitation capability — path 2 needs host support."
        }, Pretty);

    static string ElicitFailed(
        McpServer server,
        (bool Supported, object? Raw) caps,
        Exception ex) =>
        JsonSerializer.Serialize(new
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
