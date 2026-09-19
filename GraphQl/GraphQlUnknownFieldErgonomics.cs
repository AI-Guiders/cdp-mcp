#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>
/// ADR-0233 agent ergonomics for unknown GraphQL fields — catalog, DidYouMean, MCP enrich.
/// Shared by <see cref="CdpGraphQlDidYouMeanFilter"/> (HC) and MCP envelope path.
/// </summary>
internal static class GraphQlUnknownFieldErgonomics
{
    const int Cap = 24;
    const string Hint = "op=voyager|type|examples on cdp_graphql; or introspection __type(name:).";

    internal static readonly string[] QueryFieldCatalog =
    [
        "textHits", "peek", "diagnostics", "goto", "session",
        "correspondence", "git", "knowledge",
        "semanticMap", "codeClones", "symbol", "testScene", "packages",
        "health", "recent", "lifecycle", "projectScene", "editorScene",
        "shellScene", "canonStack", "lastBuild", "lastTest",
    ];

    internal static bool IsUnknownField(IError error)
    {
        if (error.Code is "HC0011" or "HC0027" or "HC0028")
            return true;
        return IsUnknownFieldMessage(error.Message);
    }

    internal static bool IsUnknownFieldMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("was not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unknown field", StringComparison.OrdinalIgnoreCase);
    }

    internal static string[] Suggest(string? message)
    {
        var catalog = QueryFieldCatalog;
        if (string.IsNullOrWhiteSpace(message))
            return catalog.Take(Cap).ToArray();

        var bad = ExtractBacktickedName(message!);
        if (string.IsNullOrEmpty(bad))
            return catalog.Take(Cap).ToArray();

        return catalog
            .OrderBy(c => Levenshtein(c, bad))
            .ThenBy(c => c, StringComparer.Ordinal)
            .Take(Math.Min(8, Cap))
            .ToArray();
    }

    /// <summary>HC validation often skips <see cref="IErrorFilter"/> — patch MCP gql JSON.</summary>
    internal static JsonElement EnrichMcpErrors(JsonElement root)
    {
        var node = JsonNode.Parse(root.GetRawText())!.AsObject();
        if (node["errors"] is not JsonArray errs) return root.Clone();

        foreach (var item in errs)
        {
            if (item is not JsonObject err) continue;
            var message = err["message"]?.GetValue<string>();
            if (!IsUnknownFieldMessage(message)) continue;

            var ext = err["extensions"] as JsonObject ?? new JsonObject();
            err["extensions"] = ext;
            ext["didYouMean"] ??= JsonSerializer.SerializeToNode(Suggest(message));
            ext["availableFields"] ??= JsonSerializer.SerializeToNode(QueryFieldCatalog);
            ext["hint"] ??= Hint;
        }

        return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    }

    internal static IError ApplyToError(IError error)
    {
        if (!IsUnknownField(error))
            return error;

        var extensions = error.Extensions is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(error.Extensions);

        extensions.TryAdd("didYouMean", Suggest(error.Message));
        extensions.TryAdd("availableFields", QueryFieldCatalog.Take(Cap).ToArray());
        extensions.TryAdd("hint", Hint);
        return error.WithExtensions(extensions);
    }

    static string? ExtractBacktickedName(string message)
    {
        var tick = message.IndexOf('`');
        if (tick < 0) return null;
        var tick2 = message.IndexOf('`', tick + 1);
        return tick2 > tick ? message[(tick + 1)..tick2] : null;
    }

    static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
