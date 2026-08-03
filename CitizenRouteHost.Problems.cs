#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent problems — sync IdeProblemsChannel; place problems organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<DocumentBufferStore, SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? ProblemsHandleOverride { get; set; }

    static Applied RunProblems(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "list" : route.Op!;
        var args = BuildProblemsArgs(route, op);

        try
        {
            object result;
            if (ProblemsHandleOverride is { } ov)
            {
                var store = new DocumentBufferStore();
                var session = SessionResolver?.Invoke() ?? new SessionContext();
                result = ov(store, session, args);
            }
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "problems",
                        Go: "problems",
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "problems",
                        Go: "problems",
                        Reason: "no_session");
                }

                result = IdeProblemsChannel.Handle(store, session, args);
            }

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftOrganOk(json);
            var pulse = TryReadSoftOrganPulse(json, "problems", op);
            var seat = IdeDeskSeats.PlaceOrgan("problems");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "problems",
                Seat: seat,
                Go: "problems",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "problems_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "problems",
                Go: "problems",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildProblemsArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "row",
            CitizenIntentRouter.ExtractKeyedValue(raw, "row")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "pick")
            ?? route.Path);
        PutIfPresent(args, "wire",
            CitizenIntentRouter.ExtractKeyedValue(raw, "wire")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at"));
        PutIfPresent(args, "pad", CitizenIntentRouter.ExtractKeyedValue(raw, "pad"));

        var aim = CitizenIntentRouter.ExtractKeyedValue(raw, "aim");
        if (aim is { Length: > 0 })
            PutIfPresent(args, "aim", aim);
        else if (args.ContainsKey("row") || args.ContainsKey("wire"))
            args["aim"] = JsonSerializer.SerializeToElement(true);

        return args;
    }
}
