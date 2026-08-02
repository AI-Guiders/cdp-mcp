#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent ide — sync IdeLanguageTools.DispatchBareAsync (goto/usages/diagnostics).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject JSON; live uses DispatchBareAsync + ByDomainResolver.</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, Task<string>>? IdeCallOverride { get; set; }

    static Applied RunIde(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "go_to_definition" : route.Op!;
        var args = BuildIdeArgs(route);

        try
        {
            string json;
            if (IdeCallOverride is { } ov)
            {
                json = ov(op, args).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "ide",
                        Go: "editor_scene",
                        Path: route.Path,
                        Reason: "no_session");
                }

                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                json = IdeLanguageTools.DispatchBareAsync(op, session, byDomain, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json) || !LooksLikeIdeError(json);
            var pulse = TryReadIdePulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "ide",
                Seat: seat,
                Go: "editor_scene",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "ide_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "ide",
                Go: "editor_scene",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildIdeArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var path = route.Path
            ?? ExtractMcpKeyed(route.Raw, "path")
            ?? ExtractMcpKeyed(route.Raw, "file_path")
            ?? ExtractMcpKeyed(route.Raw, "file");
        if (path is { Length: > 0 })
            args["file_path"] = JsonSerializer.SerializeToElement(path);

        if (TryParseIdeInt(route.Raw, "line", "l", out var line))
            args["line"] = JsonSerializer.SerializeToElement(line);
        if (TryParseIdeInt(route.Raw, "column", "col", out var col))
            args["column"] = JsonSerializer.SerializeToElement(col);
        else if (!args.ContainsKey("column") && route.Op is "go_to_definition" or "find_usages")
            args["column"] = JsonSerializer.SerializeToElement(1);

        if (ExtractMcpKeyed(route.Raw, "scope") is { Length: > 0 } scope)
            args["scope"] = JsonSerializer.SerializeToElement(scope);

        return args;
    }

    static bool TryParseIdeInt(string raw, string key, string alias, out int value)
    {
        value = 0;
        var s = ExtractMcpKeyed(raw, key) ?? ExtractMcpKeyed(raw, alias);
        return s is { Length: > 0 } && int.TryParse(s, out value);
    }

    static bool LooksLikeIdeError(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return true;
        if (json.Contains("\"ok\":false", StringComparison.Ordinal)
            || json.Contains("\"ok\": false", StringComparison.Ordinal))
            return true;
        return json.StartsWith("error", StringComparison.OrdinalIgnoreCase);
    }

    static string? TryReadIdePulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            if (root.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {locs.GetArrayLength()} loc(s)");

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {items.GetArrayLength()} item(s)");

            if (root.TryGetProperty("diagnostics", out var diags) && diags.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · diags · {diags.GetArrayLength()}");

            if (root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var n))
                return TruncPulse($"ide · diags · errors={n}");
        }
        catch
        {
            /* best-effort */
        }

        return TruncPulse($"ide · {ShortIdeOp(op)}");
    }

    static string ShortIdeOp(string op) =>
        op switch
        {
            "go_to_definition" => "goto",
            "find_usages" => "usages",
            "get_diagnostics" => "diags",
            _ => op
        };
}
