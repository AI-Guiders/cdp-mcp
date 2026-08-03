#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent project|sln — sync MetaDispatch cdp_project_*/cdp_sln_*; place project organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake project JSON; live uses MetaDispatchResolver("cdp_project_*"|"cdp_sln_*", …).</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, string>? ProjectDispatchOverride { get; set; }

    static Applied RunProjSln(CitizenIntentRouter.Route route)
    {
        var family = string.IsNullOrWhiteSpace(route.Organ) ? "project" : route.Organ!;
        var op = string.IsNullOrWhiteSpace(route.Op)
            ? (family == "sln" ? "list" : "scene")
            : route.Op!;
        var tool = MapProjectTool(family, op);
        var args = BuildProjectArgs(route, family, op);

        try
        {
            string json;
            if (ProjectDispatchOverride is { } ov)
            {
                json = ov(tool, args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                json = meta(tool, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json) || TryReadProjectOk(json);
            var pulse = TryReadProjectPulse(json, family, op, route.Path);
            var seat = IdeDeskSeats.PlaceOrgan("project");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "project",
                Seat: seat,
                Go: "project",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "project_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "project",
                Go: "project",
                Path: route.Path,
                Reason: "project_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "project",
                Go: "project",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string MapProjectTool(string family, string op) =>
        family == "sln"
            ? op switch
            {
                "create" => "cdp_sln_create",
                "projects" => "cdp_sln_projects",
                "add" => "cdp_sln_add",
                "remove" => "cdp_sln_remove",
                _ => "cdp_sln_list"
            }
            : op switch
            {
                "list" => "cdp_project_list",
                "create" => "cdp_project_create",
                "close" => "cdp_project_close",
                "add" => "cdp_project_add_to_sln",
                _ => "cdp_project_scene"
            };

    static Dictionary<string, JsonElement> BuildProjectArgs(
        CitizenIntentRouter.Route route, string family, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var raw = route.Raw;

        if (op is "scene" or "list")
        {
            PutIfPresent(args, "root", route.Path
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "root"));
            if (op is "scene")
            {
                PutBoolIfPresent(args, "include_installed",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "include_installed"));
                PutIntIfPresent(args, "max_existing",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "max_existing"));
                PutIntIfPresent(args, "max_installed",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "max_installed"));
            }
        }
        else if (op is "create")
        {
            PutIfPresent(args, "output_dir", route.Path
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "output_dir"));
            PutIfPresent(args, "name", route.Tool
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "name"));
            if (family == "project")
            {
                PutIfPresent(args, "template", route.Detail
                    ?? CitizenIntentRouter.ExtractKeyedValue(raw, "template"));
                PutIfPresent(args, "tfm_policy",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "tfm_policy"));
                PutIfPresent(args, "tfm",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "tfm"));
                PutIfPresent(args, "engine_policy",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "engine_policy"));
                PutIfPresent(args, "engines",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "engines"));
            }

            PutBoolIfPresent(args, "force",
                CitizenIntentRouter.ExtractKeyedValue(raw, "force"));
            PutBoolIfPresent(args, "open",
                CitizenIntentRouter.ExtractKeyedValue(raw, "open"));
        }
        else if (op is "add" or "remove")
        {
            PutIfPresent(args, "project", route.Path
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "project"));
            PutIfPresent(args, "solution", route.Scene
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "solution"));
            if (op is "add")
            {
                PutBoolIfPresent(args, "in_root",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "in_root"));
                PutIfPresent(args, "solution_folder",
                    CitizenIntentRouter.ExtractKeyedValue(raw, "solution_folder"));
            }
        }
        else if (op is "projects")
        {
            PutIfPresent(args, "solution", route.Scene
                ?? route.Path
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "solution"));
        }

        return args;
    }

    static bool TryReadProjectOk(string json)
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
            return root.TryGetProperty("kind", out _) || root.TryGetProperty("summary", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadProjectPulse(string json, string family, string op, string? path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            var bits = new List<string> { family, op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("summary", out var sum) && sum.ValueKind == JsonValueKind.String
                && sum.GetString() is { Length: > 0 } s)
                bits.Add(s);
            else if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            if (path is { Length: > 0 })
                bits.Add(path);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(family + " " + op);
        }
    }
}
