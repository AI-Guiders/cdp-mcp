#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent md_author — sync IdeMdAuthorChannel; place md_author organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? MdAuthorHandleOverride { get; set; }

    static Applied RunMdAuthor(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && MdAuthorHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "md_author",
                Go: "md_author",
                Reason: "no_session");
        }

        var args = BuildMdAuthorArgs(route, op);

        try
        {
            object result;
            if (MdAuthorHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeMdAuthorChannel.HandleJson(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftOrganOk(json);
            var pulse = TryReadSoftOrganPulse(json, "md_author", op);
            var seat = IdeDeskSeats.PlaceOrgan("md_author");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "md_author",
                Seat: seat,
                Go: "md_author",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "md_author_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "md_author",
                Go: "md_author",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildMdAuthorArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "path",
            route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path"));
        PutIfPresent(args, "out", CitizenIntentRouter.ExtractKeyedValue(raw, "out"));
        PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(raw, "scope"));
        return args;
    }
}
