#nullable enable
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Unknown field → Did you mean + available fields (capped) — ADR-0233.</summary>
internal sealed class CdpGraphQlDidYouMeanFilter : IErrorFilter
{
    const int Cap = 24;

    public IError OnError(IError error)
    {
        if (error.Code is not ("HC0011" or "HC0027" or "HC0028"))
        {
            // Also catch message-shaped unknown field errors
            var msg = error.Message ?? "";
            if (!msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                && !msg.Contains("was not found", StringComparison.OrdinalIgnoreCase)
                && !msg.Contains("Unknown field", StringComparison.OrdinalIgnoreCase))
                return error;
        }

        var extensions = error.Extensions is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(error.Extensions);

        if (!extensions.ContainsKey("didYouMean"))
            extensions["didYouMean"] = Suggest(error.Message);
        if (!extensions.ContainsKey("hint"))
            extensions["hint"] = "op=voyager|type|examples on cdp_graphql; or introspection __type(name:)."
;

        return error.WithExtensions(extensions);
    }

    static string[] Suggest(string? message)
    {
        // Lightweight: surface common Query fields when message mentions a typo-ish name.
        var catalog = new[]
        {
            "textHits", "peek", "diagnostics", "goto", "symbol",
            "correspondence", "semanticMap", "git", "knowledge", "packages", "testScene", "session"
        };
        if (string.IsNullOrWhiteSpace(message))
            return catalog.Take(Cap).ToArray();

        // Extract `Foo` from typical HC messages
        var tick = message!.IndexOf('`');
        string? bad = null;
        if (tick >= 0)
        {
            var tick2 = message.IndexOf('`', tick + 1);
            if (tick2 > tick)
                bad = message[(tick + 1)..tick2];
        }

        if (string.IsNullOrEmpty(bad))
            return catalog.Take(Cap).ToArray();

        return catalog
            .OrderBy(c => Levenshtein(c, bad))
            .ThenBy(c => c, StringComparer.Ordinal)
            .Take(Math.Min(8, Cap))
            .ToArray();
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
