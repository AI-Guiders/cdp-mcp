#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent test_scene — sync MetaDispatch cdp_test_scene; place test_scene organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake test_scene JSON; live uses MetaDispatchResolver("cdp_test_scene", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? TestSceneDispatchOverride { get; set; }

    static Applied RunTestScene(CitizenIntentRouter.Route route)
    {
        var args = BuildTestSceneArgs(route);

        try
        {
            string json;
            if (TestSceneDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                json = meta("cdp_test_scene", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadTestSceneOk(json);
            var pulse = TryReadTestScenePulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("test_scene");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "test_scene",
                Seat: seat,
                Go: "test_scene",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "test_scene_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test_scene",
                Go: "test_scene",
                Path: route.Path,
                Reason: "test_scene_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test_scene",
                Go: "test_scene",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildTestSceneArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var raw = route.Raw;

        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "solution_path", CitizenIntentRouter.ExtractKeyedValue(raw, "solution_path"));
        PutIfPresent(args, "configuration", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "configuration"));

        var maxTests = route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "max_tests");
        if (maxTests is { Length: > 0 } && int.TryParse(maxTests, out var max))
            args["max_tests"] = JsonSerializer.SerializeToElement(max);

        var timeout = CitizenIntentRouter.ExtractKeyedValue(raw, "timeout_seconds");
        if (timeout is { Length: > 0 } && int.TryParse(timeout, out var sec))
            args["timeout_seconds"] = JsonSerializer.SerializeToElement(sec);

        return args;
    }

    static bool TryReadTestSceneOk(string json)
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
                || root.TryGetProperty("tests", out _)
                || root.TryGetProperty("discovered", out _)
                || root.TryGetProperty("last_run", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadTestScenePulse(string json)
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
                return TruncPulse("test_scene · " + e);

            return TruncPulse("test_scene · map");
        }
        catch
        {
            return null;
        }
    }
}
