#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent plugins — sync IdePluginsChannel; place plugins organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<DocumentBufferStore, SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? PluginsHandleOverride { get; set; }

    static Applied RunPlugins(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "list" : route.Op!;
        var args = BuildPluginsArgs(route, op);

        try
        {
            object result;
            if (PluginsHandleOverride is { } ov)
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
                        Action: "plugins",
                        Go: "plugins",
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "plugins",
                        Go: "plugins",
                        Reason: "no_session");
                }

                result = IdePluginsChannel.Handle(store, session, args);
            }

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadSoftInstrumentOk(json);
            var pulse = TryReadSoftInstrumentPulse(json, "plugins", op);
            var seat = IdeDeskSeats.PlaceOrgan("plugins");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "plugins",
                Seat: seat,
                Go: "plugins",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "plugins_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "plugins",
                Go: "plugins",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildPluginsArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "id",
            CitizenIntentRouter.ExtractKeyedValue(raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "plugin")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "name")
            ?? route.Path);
        PutIfPresent(args, "q",
            CitizenIntentRouter.ExtractKeyedValue(raw, "q")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? route.Path);
        PutIfPresent(args, "group", CitizenIntentRouter.ExtractKeyedValue(raw, "group"));
        PutIfPresent(args, "vsix", CitizenIntentRouter.ExtractKeyedValue(raw, "vsix"));
        PutIfPresent(args, "url", CitizenIntentRouter.ExtractKeyedValue(raw, "url"));
        return args;
    }
}
