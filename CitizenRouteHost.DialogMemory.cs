#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent dialog — scene/history/clear Face dialog jsonl (+ optional sticky).</summary>
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
            ? "dialog · cleared · sticky cleared"
            : "dialog · cleared";
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "clear",
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
