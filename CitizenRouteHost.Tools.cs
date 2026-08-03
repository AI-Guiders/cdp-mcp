#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent tools — sync MetaDispatch cdp_tools; place tools organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake tools JSON; live uses MetaDispatchResolver("cdp_tools", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? ToolsDispatchOverride { get; set; }

    static Applied RunTools(CitizenIntentRouter.Route route)
    {
        var args = BuildToolsArgs(route);

        try
        {
            string json;
            if (ToolsDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_tools", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadToolsOk(json);
            var pulse = TryReadToolsPulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("tools");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "tools",
                Seat: seat,
                Go: "tools",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "tools_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "tools",
                Go: "tools",
                Reason: "tools_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "tools",
                Go: "tools",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildToolsArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        PutIfPresent(args, "phase", route.Scene
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "phase"));
        PutIfPresent(args, "object", route.Organ
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "object")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "obj"));
        PutIfPresent(args, "intent", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "intent"));
        PutIfPresent(args, "language", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "language")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "lang"));

        var limit = route.Cmd
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "limit");
        if (!string.IsNullOrWhiteSpace(limit) && int.TryParse(limit.Trim(), out var lim))
            args["limit"] = JsonSerializer.SerializeToElement(lim);

        return args;
    }

    static bool TryReadToolsOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("tools", out _)
                || root.TryGetProperty("total", out _)
                || root.TryGetProperty("phase", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadToolsPulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("tools · " + e);

            var phase = "?";
            var obj = "?";
            if (root.TryGetProperty("phase", out var ph) && ph.ValueKind == JsonValueKind.String
                && ph.GetString() is { Length: > 0 } phaseVal)
                phase = phaseVal;
            if (root.TryGetProperty("object", out var o) && o.ValueKind == JsonValueKind.String
                && o.GetString() is { Length: > 0 } objVal)
                obj = objVal;

            var n = "?";
            if (root.TryGetProperty("total", out var totalEl))
            {
                if (totalEl.ValueKind == JsonValueKind.Number && totalEl.TryGetInt32(out var ti))
                    n = ti.ToString();
                else if (totalEl.ValueKind == JsonValueKind.String && totalEl.GetString() is { Length: > 0 } ts)
                    n = ts;
            }
            else if (root.TryGetProperty("tools", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array)
                n = toolsEl.GetArrayLength().ToString();

            var lang = "";
            if (root.TryGetProperty("language", out var langEl) && langEl.ValueKind == JsonValueKind.String
                && langEl.GetString() is { Length: > 0 } langVal)
                lang = " · " + langVal;

            return TruncPulse("tools · " + phase + "/" + obj + " · n=" + n + lang);
        }
        catch
        {
            return TruncPulse("tools");
        }
    }
}
