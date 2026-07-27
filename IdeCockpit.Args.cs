#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Cockpit arg/json property helpers.</summary>
internal static partial class IdeCockpit
{
    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static string? PropStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static bool? PropBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
            ? p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    static int? PropInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        return null;
    }

    static string? Truncate(string? s, int max)
    {
        if (s is null)
            return null;
        return s.Length <= max ? s : s[..max] + "…";
    }

    static IReadOnlyDictionary<string, JsonElement> WithStringArg(
        IReadOnlyDictionary<string, JsonElement> args,
        string key,
        string value)
    {
        var d = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        d[key] = JsonSerializer.SerializeToElement(value);
        return d;
    }

}
