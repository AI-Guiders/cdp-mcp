#nullable enable
using System.Text.Json;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

internal static partial class IdeCockpitSoftDispatch
{
    static readonly SoftOrganAliasCatalog SoftAliases = new();
    static readonly SoftOrganBoardMetaCatalog SoftMeta = new();

    static bool IsGo(string? goVerb, params string[] aliases) =>
        goVerb is { Length: > 0 } &&
        aliases.Any(alias => goVerb.Equals(alias, StringComparison.OrdinalIgnoreCase));

    static bool IsSoft(string? goVerb, SoftOrganKind kind) =>
        SoftAliases.TryResolve(goVerb) == kind;

    static void PlaceAndClear(ref string? goVerb, string organ)
    {
        if (IdeDeskSeats.IsSeatsMode())
            IdeDeskSeats.PlaceOrgan(organ);
        goVerb = null;
    }

    static void PlaceSoft(ref string? goVerb, SoftOrganKind kind) =>
        PlaceAndClear(ref goVerb, SoftMeta.Require(kind).Go);

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>
    /// Cockpit passes nested <c>go_args</c>; soft organs expect flat organ keys (op/path/…).
    /// </summary>
    static IReadOnlyDictionary<string, JsonElement> OrganArgs(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var flat = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key.Equals("go_args", StringComparison.OrdinalIgnoreCase))
                continue;
            flat[kv.Key] = kv.Value;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
                flat[p.Name] = p.Value.Clone();
        }

        return flat;
    }
}
