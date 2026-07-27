#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeElicit
{
    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
