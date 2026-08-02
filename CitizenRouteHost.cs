#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Host execute for <see cref="CitizenIntentRouter.Route"/> — seat place + buffer open/replace + plan REPL + build + test + mcp.
/// Sync host path; <c>@intent build|test|mcp</c> wait lifecycle/outlet (bounded) — not cockpit W-spray.
/// </summary>
internal static partial class CitizenRouteHost
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
        string? Cmd = null,
        string? Pulse = null,
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
                Cmd: route.Cmd,
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
            CitizenIntentRouter.Verb.Replace => ReplaceInPath(route),
            CitizenIntentRouter.Verb.Build => RunBuild(route),
            CitizenIntentRouter.Verb.Test => RunTest(route),
            CitizenIntentRouter.Verb.Mcp => RunMcp(route),
            CitizenIntentRouter.Verb.Shell => RunShell(route),
            CitizenIntentRouter.Verb.Cmd => RunPlanCmd(route),
            CitizenIntentRouter.Verb.Refuse => new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "refuse",
                Cmd: route.Cmd,
                Reason: route.Reason),
            _ => new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "skip",
                Reason: route.Reason ?? "unrecognized")
        };
    }

    static Applied RunPlanCmd(CitizenIntentRouter.Route route)
    {
        var cmd = route.Cmd?.Trim() ?? "";
        if (cmd.Length == 0)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "repl",
                Reason: "cmd_empty");
        }

        try
        {
            var applied = IdeRepl.Apply(cmd, new Dictionary<string, JsonElement>(StringComparer.Ordinal));
            if (applied is null)
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "repl",
                    Cmd: cmd,
                    Reason: "repl_null");
            }

            var (args, direct) = applied.Value;
            if (direct is not null)
            {
                var err = TryReadCclError(direct);
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "repl",
                    Cmd: cmd,
                    Go: "plan",
                    Reason: err ?? "ccl_direct");
            }

            if (!args.TryGetValue("tm_op", out var tmEl)
                || tmEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(tmEl.GetString()))
            {
                if (args.TryGetValue("go", out var goEl)
                    && goEl.ValueKind == JsonValueKind.String
                    && goEl.GetString() is { Length: > 0 } goOnly)
                {
                    var placedOnly = IdeDeskSeats.PlaceOrgan(goOnly);
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: placedOnly is not null,
                        Action: "repl_place",
                        Seat: placedOnly,
                        Go: IdeDeskSeats.CanonicalOrganPin(goOnly),
                        Cmd: cmd,
                        Reason: placedOnly is null ? "place_failed" : null);
                }

                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "repl",
                    Cmd: cmd,
                    Reason: "no_tm_op");
            }

            if (!IdeStageCycle.TryWorkspace(out var store, out var state, out var phase))
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "repl",
                    Cmd: cmd,
                    Go: "plan",
                    Reason: "no_workspace");
            }

            var tmArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
            if (phase is { Length: > 0 })
                tmArgs["session_phase"] = JsonSerializer.SerializeToElement(phase);
            var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
            if (root is { Length: > 0 })
                tmArgs["project_root"] = JsonSerializer.SerializeToElement(root);

            var result = IdeTaskManager.Handle(store, state, tmArgs);
            var pulse = TryReadPulse(result);
            var ok = TryReadOk(result);
            var seat = IdeDeskSeats.PlaceOrgan("plan");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "repl",
                Seat: seat,
                Go: "plan",
                Cmd: cmd,
                Pulse: pulse,
                Reason: ok ? null : (TryReadError(result) ?? pulse ?? "tm_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "repl",
                Cmd: cmd,
                Go: "plan",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string? TryReadCclError(object direct)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(direct));
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
            {
                var err = e.GetString();
                if (root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String)
                    return err + " · " + h.GetString();
                return err;
            }
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadPulse(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadError(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.String
                && e.GetString() is { Length: > 0 } err)
                return err.Trim();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static bool TryReadOk(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("ok", out var o)
                && o.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return o.GetBoolean();
        }
        catch
        {
            /* assume ok if unreadable */
        }

        return true;
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
        PublishGlassLandOpen(full);
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
