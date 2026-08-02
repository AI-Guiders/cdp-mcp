#nullable enable

namespace CdpMcp;

/// <summary>
/// Host execute for <see cref="CitizenIntentRouter.Route"/> — seat place + buffer open.
/// Sync only; no full cockpit BuildAsync (avoids W-spray / hang on turn).
/// </summary>
internal static class CitizenRouteHost
{
    public sealed record Applied(
        string Raw,
        string Verb,
        bool Ok,
        string? Action = null,
        string? Seat = null,
        string? Go = null,
        string? Path = null,
        string? DocId = null,
        string? Reason = null);

    public static IReadOnlyList<Applied> Execute(IEnumerable<CitizenIntentRouter.Route>? routes)
    {
        if (routes is null)
            return [];

        var list = new List<Applied>();
        foreach (var route in routes)
            list.Add(ApplyOne(route));
        return list;
    }

    static Applied ApplyOne(CitizenIntentRouter.Route route)
    {
        if (!route.Ok)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: route.Verb == CitizenIntentRouter.Verb.Refuse ? "refuse" : "skip",
                Reason: route.Reason ?? "route_not_ok");
        }

        return route.Verb switch
        {
            CitizenIntentRouter.Verb.Go
                or CitizenIntentRouter.Verb.Drill
                or CitizenIntentRouter.Verb.Detail
                => PlaceGo(route),
            CitizenIntentRouter.Verb.PaneFull => NotePaneFull(route),
            CitizenIntentRouter.Verb.Open => OpenPath(route),
            CitizenIntentRouter.Verb.Refuse => new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "refuse",
                Reason: route.Reason),
            _ => new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "skip",
                Reason: route.Reason ?? "unrecognized")
        };
    }

    static Applied PlaceGo(CitizenIntentRouter.Route route)
    {
        var go = route.Go;
        if (string.IsNullOrWhiteSpace(go))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "place",
                Reason: "go_empty");
        }

        try
        {
            var seat = IdeDeskSeats.PlaceOrgan(go);
            if (seat is null)
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "place",
                    Go: go,
                    Reason: "place_failed");
            }

            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: true,
                Action: "place",
                Seat: seat,
                Go: IdeDeskSeats.CanonicalOrganPin(go));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "place",
                Go: go,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied NotePaneFull(CitizenIntentRouter.Route route)
    {
        var seat = IdeDeskSeats.NormalizeSeatId(route.Organ);
        if (seat is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "pane_full",
                Reason: "pane_full_seat_invalid");
        }

        // Seat dump needs cockpit pulse (pane_full=) — place cockpit organ on M as pointer.
        var placed = IdeDeskSeats.PlaceOrgan("cockpit");
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "pane_full",
            Seat: seat,
            Go: "cockpit",
            Reason: placed is null
                ? "seat_noted — cockpit pane_full=" + seat
                : "seat_noted + cockpit@" + placed + " — cockpit pane_full=" + seat);
    }

    static Applied OpenPath(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "open",
                Reason: "open_path_empty");
        }

        var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
        if (!IdeLanguageTools.TryOpenDocument(path, root, out var full, out var docId, out var error))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "open",
                Path: path,
                Reason: error ?? "open_failed");
        }

        var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "open",
            Seat: seat,
            Go: "editor_scene",
            Path: full,
            DocId: docId);
    }
}
