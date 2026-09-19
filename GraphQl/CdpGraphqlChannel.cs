#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp.GraphQl;

/// <summary>
/// MCP organ cdp_graphql — thin ADR-0198 facade over in-proc HotChocolate (ADR-0233).
/// Prefer op=voyager|examples before query=.
/// </summary>
internal static class CdpGraphqlChannel
{
    public const string SchemaVersion = "cdp_graphql/v0";
    public const string ToolName = "cdp_graphql";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        DocumentBufferStore docStore,
        CdpSettings settings,
        IReadOnlyDictionary<string, JsonElement>? args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? dispatchToolAsync = null) =>
        JsonSerializer.Serialize(Handle(session, docStore, settings, args, dispatchToolAsync), Pretty);

    public static object Handle(
        SessionContext session,
        DocumentBufferStore docStore,
        CdpSettings settings,
        IReadOnlyDictionary<string, JsonElement>? args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? dispatchToolAsync = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? (Opt(args, "query") is { Length: > 0 } ? "query" : "voyager"))
            .Trim().ToLowerInvariant();

        return op switch
        {
            "voyager" or "toc" or "map" or "type" => GraphQlAgentVoyager.Frame(
                typeName: Opt(args, "name") ?? Opt(args, "type"),
                filter: Opt(args, "filter") ?? Opt(args, "q") ?? Opt(args, "find"),
                anchorWire: Opt(args, "anchor") ?? Opt(args, "at") ?? Opt(args, "wire"),
                detail: Opt(args, "detail") ?? Opt(args, "go_detail")),
            "examples" or "goldens" => ExamplesCard(),
            "query" or "gql" or "run" => QueryCard(session, docStore, settings, args, dispatchToolAsync),
            _ => new
            {
                ok = false,
                schema = SchemaVersion,
                error = "unknown_op",
                hint = "op=voyager|examples|query — voyager: type=|filter=|anchor=|detail=pulse|full; query= for documents"
            }
        };
    }

    static object ExamplesCard() => new
    {
        ok = true,
        schema = SchemaVersion,
        examples = new object[]
        {
            new { id = 1, title = "textHits → anchor", query = "{ textHits(query: \"IdeFindChannel\", first: 5) { nodes { path line preview anchor { wire file lineStart } } totalCount } }" },
            new { id = 2, title = "peek → anchor", query = "{ peek(path: \"GraphQl/CdpQueryRoot.cs\", limit: 20) { path lines { n text anchor { wire } } } }" },
            new { id = 3, title = "goto → anchor", query = "{ goto(query: \"t CdpQueryType\", first: 5) { kind name score anchor { wire } } }" },
            new { id = 4, title = "diagnostics → Fix chain", query = "{ diagnostics(path: \"GraphQl\", first: 10) { severity message path line anchor { wire } } }" },
            new { id = 5, title = "session + git pulse", query = "{ session { phase projectRoot solutionOrProjectPath language } git { ok schema } }" },
            new { id = 6, title = "Vision-Exp external tree", query = "{ textHits(query: \"AddGraphQLServer\", scope: \"external\", path: \"C:/Projects/EDW.Portal.Repo\", first: 10) { nodes { path preview anchor { wire } } } }" },
            new { id = 7, title = "LIKE translator", query = "{ textHits(like: \"%FindInFiles%\", first: 5) { nodes { preview anchor { wire } } } }" },
            new { id = 8, title = "Vision-Exp peek OOW", query = "{ peek(path: \"C:/Windows/System32/drivers/etc/hosts\", limit: 5) { path lines { n text anchor { wire } } } }" },
        }
    };

    static object QueryCard(
        SessionContext session,
        DocumentBufferStore docStore,
        CdpSettings settings,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? dispatchToolAsync = null)
    {
        var query = Opt(args, "query") ?? Opt(args, "gql") ?? Opt(args, "q");
        if (string.IsNullOrWhiteSpace(query))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                error = "query_required",
                hint = "op=query query='{ textHits(query:\"x\", first:5) { nodes { preview } } }'"
            };
        }

        var outcome = CdpGraphQlQueryRunner.Run(session, docStore, settings, query!, args, dispatchToolAsync);
        if (outcome.Error is not null)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                error = outcome.Error,
                detail = outcome.Detail,
                hint = outcome.Hint
            };
        }

        return new { ok = outcome.Ok, schema = SchemaVersion, gql = outcome.Gql };
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
