#nullable enable
using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

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

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(args), Pretty);

    public static object Handle(IReadOnlyDictionary<string, JsonElement>? args)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? (Opt(args, "query") is { Length: > 0 } ? "query" : "voyager"))
            .Trim().ToLowerInvariant();

        return op switch
        {
            "voyager" or "toc" or "map" => VoyagerCard(),
            "type" => TypeCard(Opt(args, "name") ?? Opt(args, "type") ?? "Query"),
            "examples" or "goldens" => ExamplesCard(),
            "query" or "gql" or "run" => QueryCard(args),
            _ => new
            {
                ok = false,
                schema = SchemaVersion,
                error = "unknown_op",
                hint = "op=voyager|type|examples|query — then query= / variables="
            }
        };
    }

    static object VoyagerCard() => new
    {
        ok = true,
        schema = SchemaVersion,
        role = "graphql",
        http = CdpGraphQlRegistration.HttpPath,
        roots = new[]
        {
            "textHits(query|like, path, scope, first)",
            "peek(path, offset, limit)",
            "diagnostics / goto / symbol / correspondence / semanticMap / git / knowledge / packages / testScene / session (L2+)"
        },
        hint = "op=examples for goldens; op=query query='{ textHits(query:\"IdeFindChannel\", first:5) { nodes { preview anchor { wire } } } }'"
    };

    static object TypeCard(string name) => new
    {
        ok = true,
        schema = SchemaVersion,
        type = name,
        hint = name.Equals("Query", StringComparison.OrdinalIgnoreCase)
            ? "Fields: textHits, peek (+ L2 roots). Anchor: wire/file/lineStart/lineEnd."
            : "Use introspection or Banana Cake Pop at /api/v1/cdp/graphql",
    };

    static object ExamplesCard() => new
    {
        ok = true,
        schema = SchemaVersion,
        examples = new object[]
        {
            new { id = 1, title = "textHits → anchor", query = "{ textHits(query: \"IdeFindChannel\", first: 5) { nodes { path line preview anchor { wire file lineStart } } totalCount } }" },
            new { id = 2, title = "peek → anchor", query = "{ peek(path: \"GraphQl/CdpQueryRoot.cs\", limit: 20) { path lines { n text anchor { wire } } } }" },
            new { id = 6, title = "Vision-Exp external tree", query = "{ textHits(query: \"AddGraphQLServer\", scope: \"external\", path: \"C:/Projects/EDW.Portal.Repo\", first: 10) { nodes { path preview anchor { wire } } } }" },
            new { id = 7, title = "LIKE translator", query = "{ textHits(like: \"%FindInFiles%\", first: 5) { nodes { preview anchor { wire } } } }" },
        }
    };

    static object QueryCard(IReadOnlyDictionary<string, JsonElement> args)
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

        var executor = CdpGraphQlRuntime.Executor;
        if (executor is null)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                error = "executor_cold",
                hint = "CdpService must be running with GraphQL warmed (deploy/restart after ADR-0233 ship)."
            };
        }

        IReadOnlyDictionary<string, object?>? variables = null;
        if (args.TryGetValue("variables", out var varsEl))
        {
            if (varsEl.ValueKind == JsonValueKind.String)
            {
                var raw = varsEl.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                    variables = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw!);
            }
            else if (varsEl.ValueKind == JsonValueKind.Object)
            {
                variables = JsonSerializer.Deserialize<Dictionary<string, object?>>(varsEl.GetRawText());
            }
        }

        var request = OperationRequestBuilder.New()
            .SetDocument(query!)
            .SetVariableValues(variables)
            .Build();

        var result = executor.ExecuteAsync(request).GetAwaiter().GetResult();
        var json = result.ToJson();
        if (result is IAsyncDisposable asyncDisp)
            asyncDisp.DisposeAsync().AsTask().GetAwaiter().GetResult();
        else if (result is IDisposable disp)
            disp.Dispose();
        using var parsed = JsonDocument.Parse(json);
        var hasErrors = parsed.RootElement.TryGetProperty("errors", out var errs)
            && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0;

        return new
        {
            ok = !hasErrors,
            schema = SchemaVersion,
            gql = parsed.RootElement.Clone()
        };
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
