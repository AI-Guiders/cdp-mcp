#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent test_plan — sync MetaDispatch cdp_test_plan; place test_plan organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake test_plan JSON; live uses MetaDispatchResolver("cdp_test_plan", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? TestPlanDispatchOverride { get; set; }

    static Applied RunTestPlan(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "preview" : route.Op!;
        var args = BuildTestPlanArgs(route, op);

        try
        {
            string json;
            if (TestPlanDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                json = meta("cdp_test_plan", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadTestPlanOk(json);
            var pulse = TryReadTestPlanPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("test_plan");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "test_plan",
                Seat: seat,
                Go: "test_plan",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "test_plan_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test_plan",
                Go: "test_plan",
                Path: route.Path,
                Reason: "test_plan_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test_plan",
                Go: "test_plan",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildTestPlanArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "solution_path", CitizenIntentRouter.ExtractKeyedValue(raw, "solution_path"));
        PutIfPresent(args, "filter", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "filter"));
        PutIfPresent(args, "configuration", CitizenIntentRouter.ExtractKeyedValue(raw, "configuration"));
        PutIfPresent(args, "detail", CitizenIntentRouter.ExtractKeyedValue(raw, "detail"));
        PutBoolIfPresent(args, "failed_first", route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "failed_first"));
        PutBoolIfPresent(args, "include_raw_output", CitizenIntentRouter.ExtractKeyedValue(raw, "include_raw_output"));

        var timeout = CitizenIntentRouter.ExtractKeyedValue(raw, "timeout_seconds");
        if (timeout is { Length: > 0 } && int.TryParse(timeout, out var sec))
            args["timeout_seconds"] = JsonSerializer.SerializeToElement(sec);

        return args;
    }

    static bool TryReadTestPlanOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("schema", out _)
                || root.TryGetProperty("selected", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadTestPlanPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("test_plan " + op + " · " + e);

            return TruncPulse("test_plan · " + op);
        }
        catch
        {
            return null;
        }
    }
}
