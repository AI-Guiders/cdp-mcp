#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeReviewChannel
{
    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null) return d;
        foreach (var (k, v) in args)
        {
            if (k.Equals("go_args", StringComparison.OrdinalIgnoreCase)
                && v.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in v.EnumerateObject())
                    d[p.Name] = p.Value.Clone();
            }
            else
                d[k] = v.Clone();
        }

        return d;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static object Err(string code, string hint) =>
        new { ok = false, go = "review", schema = SchemaVersion, error = code, hint };
}
