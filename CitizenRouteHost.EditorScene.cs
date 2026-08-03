#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent editor_scene — sync MetaDispatch cdp_editor_scene; place editor_scene organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake editor_scene JSON; live uses MetaDispatchResolver("cdp_editor_scene", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? EditorSceneDispatchOverride { get; set; }

    static Applied RunEditorScene(CitizenIntentRouter.Route route)
    {
        var args = BuildEditorSceneArgs(route);

        try
        {
            string json;
            if (EditorSceneDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_editor_scene", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadEditorSceneOk(json);
            var pulse = TryReadEditorScenePulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "editor_scene",
                Seat: seat,
                Go: "editor_scene",
                Path: route.Path,
                DocId: route.Tool,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "editor_scene_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "editor_scene",
                Go: "editor_scene",
                Path: route.Path,
                Reason: "editor_scene_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "editor_scene",
                Go: "editor_scene",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildEditorSceneArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var raw = route.Raw;

        PutIfPresent(args, "detail", route.Op
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "detail"));
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file_path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "doc_id", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "doc_id")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "doc"));
        PutIfPresent(args, "locus", route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "locus")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "focus"));

        var startLine = CitizenIntentRouter.ExtractKeyedValue(raw, "start_line");
        if (startLine is { Length: > 0 } && int.TryParse(startLine, out var start))
            args["start_line"] = JsonSerializer.SerializeToElement(start);

        var endLine = CitizenIntentRouter.ExtractKeyedValue(raw, "end_line");
        if (endLine is { Length: > 0 } && int.TryParse(endLine, out var end))
            args["end_line"] = JsonSerializer.SerializeToElement(end);

        var contextLines = route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "context_lines");
        if (contextLines is { Length: > 0 } && int.TryParse(contextLines, out var ctx))
            args["context_lines"] = JsonSerializer.SerializeToElement(ctx);

        return args;
    }

    static bool TryReadEditorSceneOk(string json)
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
                || root.TryGetProperty("loci", out _)
                || root.TryGetProperty("map", out _)
                || root.TryGetProperty("counts", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadEditorScenePulse(string json)
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
                return TruncPulse("editor_scene · " + e);

            return TruncPulse("editor_scene · map");
        }
        catch
        {
            return null;
        }
    }
}
