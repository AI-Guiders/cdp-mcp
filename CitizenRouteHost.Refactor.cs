#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent refactor — sync MetaDispatch cdp_refactor; place refactor_plan organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake refactor JSON; live uses MetaDispatchResolver("cdp_refactor", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? RefactorDispatchOverride { get; set; }

    static Applied RunRefactor(CitizenIntentRouter.Route route)
    {
        var args = BuildRefactorArgs(route);

        try
        {
            string json;
            if (RefactorDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_refactor", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadRefactorOk(json);
            var pulse = TryReadRefactorPulse(json, route.Op ?? "plan");
            var seat = IdeDeskSeats.PlaceOrgan("refactor_plan");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "refactor",
                Seat: seat,
                Go: "refactor_plan",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "refactor_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "refactor",
                Go: "refactor_plan",
                Reason: "refactor_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "refactor",
                Go: "refactor_plan",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildRefactorArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "op")
            ?? "plan";
        args["op"] = JsonSerializer.SerializeToElement(op.Trim().ToLowerInvariant());

        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "locus"));
        PutIfPresent(args, "scope", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope"));
        PutIfPresent(args, "max", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "max"));
        PutIfPresent(args, "budget", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "budget"));
        return args;
    }

    static bool TryReadRefactorOk(string json)
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
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("recommend", out _)
                || root.TryGetProperty("debt", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadRefactorPulse(string json, string op)
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
                return TruncPulse("refactor · " + e);

            return TruncPulse("refactor · " + op);
        }
        catch
        {
            return TruncPulse("refactor · " + op);
        }
    }
}
