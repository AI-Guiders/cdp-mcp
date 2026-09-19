#nullable enable
using System.Collections.Concurrent;

namespace CdpMcp;

/// <summary>
/// Thin fail-streak ledger — invent / FileNotFound ×N on take → host soft-refuse (L3 Just Culture).
/// Reason = repeat-fail habitat gap; force= escape.
/// </summary>
internal static class CitizenFailStreakLedger
{
    public const int DefaultThreshold = 3;
    public const string RefuseFailStreakTake = "fail_streak_take";

    static readonly ConcurrentDictionary<string, int> ByKey = new(StringComparer.OrdinalIgnoreCase);

    public static void NoteFailure(string kind, string? pathOrNeedle)
    {
        var key = Key(kind, pathOrNeedle);
        ByKey.AddOrUpdate(key, 1, (_, n) => n + 1);
    }

    public static void NoteSuccess(string kind, string? pathOrNeedle)
    {
        ByKey.TryRemove(Key(kind, pathOrNeedle), out _);
    }

    public static int Count(string kind, string? pathOrNeedle) =>
        ByKey.TryGetValue(Key(kind, pathOrNeedle), out var n) ? n : 0;

    public static CitizenRouteHost.Applied? TryRefuseTake(CitizenIntentRouter.Route route)
    {
        if (HasForce(route))
            return null;

        var needle = route.Path ?? "?";
        var n = Count("take", needle);
        if (n < DefaultThreshold)
            return null;

        return new CitizenRouteHost.Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: false,
            Action: "refuse",
            Path: route.Path,
            Go: "find_desk",
            Pulse: RefuseFailStreakTake + " · ×" + n,
            Reason: RefuseFailStreakTake +
                    ": repeat take miss (FileNotFound/invent) — dig via find/open first; force=true escape. Just Culture: habitat gap, not blame.");
    }

    public static void ResetForTests() => ByKey.Clear();

    static string Key(string kind, string? pathOrNeedle) =>
        kind.Trim().ToLowerInvariant() + "|" + (pathOrNeedle ?? "").Trim();

    static bool HasForce(CitizenIntentRouter.Route route)
    {
        var f = CitizenIntentRouter.ExtractKeyedValue(route.Raw ?? "", "force");
        if (string.IsNullOrWhiteSpace(f))
            return false;
        if (bool.TryParse(f, out var b))
            return b;
        return f.Equals("1", StringComparison.OrdinalIgnoreCase)
               || f.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
