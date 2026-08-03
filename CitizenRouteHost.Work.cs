#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent work — sync MetaDispatch cdp_work; place intent_workspace organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake work JSON; live uses MetaDispatchResolver("cdp_work", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? WorkDispatchOverride { get; set; }

    static Applied RunWork(CitizenIntentRouter.Route route)
    {
        var args = BuildWorkArgs(route);

        try
        {
            string json;
            if (WorkDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_work", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadWorkOk(json);
            var pulse = TryReadWorkPulse(json, route.Op ?? "status");
            var seat = IdeDeskSeats.PlaceOrgan("intent_workspace");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "work",
                Seat: seat,
                Go: "intent_workspace",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "work_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "work",
                Go: "intent_workspace",
                Reason: "work_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "work",
                Go: "intent_workspace",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildWorkArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "op")
            ?? "status";
        args["op"] = JsonSerializer.SerializeToElement(op.Trim().ToLowerInvariant());

        PutIfPresent(args, "title", route.Scene
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "title"));
        PutIfPresent(args, "intent_id", route.Organ
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "intent_id"));
        PutIfPresent(args, "stage_id", route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "stage_id"));
        PutIfPresent(args, "name", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scene_name"));
        PutIfPresent(args, "status", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "status"));
        return args;
    }

    static bool TryReadWorkOk(string json)
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
            return root.TryGetProperty("database_path", out _)
                || root.TryGetProperty("active_intent_id", out _)
                || root.TryGetProperty("intents", out _)
                || root.TryGetProperty("stages", out _)
                || root.TryGetProperty("scenes", out _)
                || root.TryGetProperty("intent_id", out _)
                || root.TryGetProperty("stage_id", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadWorkPulse(string json, string op)
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
                return TruncPulse("work · " + e);

            var intent = "?";
            if (root.TryGetProperty("active_intent_title", out var it) && it.ValueKind == JsonValueKind.String
                && it.GetString() is { Length: > 0 } itv)
                intent = itv;
            else if (root.TryGetProperty("intents", out var intents) && intents.ValueKind == JsonValueKind.Array)
                intent = "n=" + intents.GetArrayLength();

            var stage = "?";
            if (root.TryGetProperty("active_stage_title", out var st) && st.ValueKind == JsonValueKind.String
                && st.GetString() is { Length: > 0 } stv)
                stage = stv;
            else if (root.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Array)
                stage = "n=" + stages.GetArrayLength();

            var scene = "?";
            if (root.TryGetProperty("active_scene_name", out var sc) && sc.ValueKind == JsonValueKind.String
                && sc.GetString() is { Length: > 0 } scv)
                scene = scv;
            else if (root.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
                scene = "n=" + scenes.GetArrayLength();

            return TruncPulse("work · " + op + " · intent=" + intent + " · stage=" + stage + " · scene=" + scene);
        }
        catch
        {
            return TruncPulse("work · " + op);
        }
    }
}
