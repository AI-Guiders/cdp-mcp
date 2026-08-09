#nullable enable

namespace CdpMcp;

/// <summary>Citizen ADCM dialog — scene/history/clear/partition/persist/rebuild (+ optional sticky wipe).</summary>
internal static partial class CitizenRouteHost
{
    static Applied RunDialogMemory(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op.Trim().ToLowerInvariant();
        var clearSticky = string.Equals(route.Cmd, "sticky", StringComparison.OrdinalIgnoreCase);

        try
        {
            return op switch
            {
                "clear" or "reset" => ClearDialogMemory(route, clearSticky),
                "partition" => PartitionDialogMemory(route, clearSticky),
                "persist" => PersistDialogMemory(route),
                "rebuild" => RebuildDialogMemory(route, clearSticky),
                "history" or "log" => SceneDialogMemory(route, history: true),
                _ => SceneDialogMemory(route, history: false)
            };
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "dialog",
                Go: "dialog",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied ClearDialogMemory(CitizenIntentRouter.Route route, bool clearSticky)
    {
        CitizenDialogHistory.Clear();
        if (clearSticky)
            CitizenStickyFacts.Clear();

        var pulse = clearSticky
            ? "dialog · pruned · sticky cleared"
            : "dialog · pruned";
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "clear",
            Go: "dialog",
            Pulse: pulse);
    }

    static Applied PartitionDialogMemory(CitizenIntentRouter.Route route, bool clearSticky)
    {
        CitizenDialogHistory.Clear();
        if (clearSticky)
            CitizenStickyFacts.Clear();

        var sticky = CitizenStickyFacts.AfferentLine();
        var pulse = clearSticky
            ? "dialog · partitioned · sticky cleared · fresh thread"
            : "dialog · partitioned · sticky kept · fresh thread";
        if (!clearSticky && !string.IsNullOrWhiteSpace(sticky))
            pulse += " · " + sticky;

        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "partition",
            Go: "dialog",
            Pulse: pulse);
    }

    static Applied PersistDialogMemory(CitizenIntentRouter.Route route)
    {
        var key = route.Path?.Trim();
        var value = route.Detail?.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "persist",
                Go: "dialog",
                Reason: "persist_needs_key_and_value");
        }

        CitizenStickyFacts.Set(key, value);
        var sticky = CitizenStickyFacts.AfferentLine() ?? ("sticky | " + key + "=" + value);
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "persist",
            Go: "dialog",
            Pulse: "dialog · persisted · " + sticky);
    }

    static Applied RebuildDialogMemory(CitizenIntentRouter.Route route, bool clearSticky)
    {
        CitizenDialogHistory.Clear();
        if (clearSticky)
            CitizenStickyFacts.Clear();

        var sticky = CitizenStickyFacts.AfferentLine();
        var pulse =
            "dialog · rebuilt · poisoned Radio wiped · dig=@intent pressure|plan|domain before invent";
        if (clearSticky)
            pulse += " · sticky cleared";
        else if (!string.IsNullOrWhiteSpace(sticky))
            pulse += " · " + sticky;

        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "rebuild",
            Go: "dialog",
            Pulse: pulse);
    }

    static Applied SceneDialogMemory(CitizenIntentRouter.Route route, bool history)
    {
        var pulse = CitizenDialogHistory.AfferentLine();
        var sticky = CitizenStickyFacts.AfferentLine();
        if (!string.IsNullOrWhiteSpace(sticky))
            pulse += " · " + sticky;

        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: history ? "history" : "scene",
            Go: "dialog",
            Pulse: pulse);
    }
}
