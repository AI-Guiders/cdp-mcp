#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent edit_plan — sync MetaDispatch cdp_edit_plan; place edit_plan organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake edit_plan JSON; live uses MetaDispatchResolver("cdp_edit_plan", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? EditPlanDispatchOverride { get; set; }

    static Applied RunEditPlan(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "draft" : route.Op!;
        var args = BuildEditPlanArgs(route, op);

        try
        {
            string json;
            if (EditPlanDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_edit_plan", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadEditPlanOk(json);
            var pulse = TryReadEditPlanPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("edit_plan");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "edit_plan",
                Seat: seat,
                Go: "edit_plan",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "edit_plan_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "edit_plan",
                Go: "edit_plan",
                Path: route.Path,
                Reason: "edit_plan_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "edit_plan",
                Go: "edit_plan",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildEditPlanArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        PutIfPresent(args, "yaml", route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "yaml")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "slices_yaml")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "plan")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "body"));
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "sketch", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "sketch"));

        PutBoolIfPresent(args, "resolve_anchors", CitizenIntentRouter.ExtractKeyedValue(raw, "resolve_anchors"));
        PutBoolIfPresent(args, "stop_on_error", CitizenIntentRouter.ExtractKeyedValue(raw, "stop_on_error"));
        PutBoolIfPresent(args, "diagnose", CitizenIntentRouter.ExtractKeyedValue(raw, "diagnose"));
        PutBoolIfPresent(args, "flush", CitizenIntentRouter.ExtractKeyedValue(raw, "flush"));
        PutBoolIfPresent(args, "skip_validate", CitizenIntentRouter.ExtractKeyedValue(raw, "skip_validate"));

        return args;
    }

    static bool TryReadEditPlanOk(string json)
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
                || root.TryGetProperty("suggested_yaml", out _)
                || root.TryGetProperty("candidates", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadEditPlanPulse(string json, string op)
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
                return TruncPulse("edit_plan " + op + " · " + e);

            return TruncPulse("edit_plan · " + op);
        }
        catch
        {
            return null;
        }
    }
}
