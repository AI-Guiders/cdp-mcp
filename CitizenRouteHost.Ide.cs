#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent ide — sync IdeLanguageTools.DispatchBareAsync (nav/complete/rename/actions).</summary>
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
        {
            var root = SessionResolver?.Invoke()?.ProjectRoot
                ?? IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
            if (!string.IsNullOrWhiteSpace(root) && !Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(root, path));
            else if (Path.IsPathRooted(path))
                path = Path.GetFullPath(path);
            args["file_path"] = JsonSerializer.SerializeToElement(path);
        }

        if (TryParseIdeInt(route.Raw, "line", "l", out var line))
            args["line"] = JsonSerializer.SerializeToElement(line);
        if (TryParseIdeInt(route.Raw, "column", "col", out var col))
            args["column"] = JsonSerializer.SerializeToElement(col);
        else if (TryParseIdeInt(route.Raw, "c", "c", out var cAlias))
            args["column"] = JsonSerializer.SerializeToElement(cAlias);
        else if (!args.ContainsKey("column") && route.Op is "go_to_definition" or "find_usages")
            args["column"] = JsonSerializer.SerializeToElement(1);

        if (TryParseIdeInt(route.Raw, "end_line", "el", out var endLine))
            args["end_line"] = JsonSerializer.SerializeToElement(endLine);
        if (TryParseIdeInt(route.Raw, "end_column", "ec", out var endCol))
            args["end_column"] = JsonSerializer.SerializeToElement(endCol);

        if (ExtractMcpKeyed(route.Raw, "scope") is { Length: > 0 } scope)
            args["scope"] = JsonSerializer.SerializeToElement(scope);

        if (ExtractMcpKeyed(route.Raw, "prefix") is { Length: > 0 } prefix)
            args["prefix"] = JsonSerializer.SerializeToElement(prefix);
        if (TryParseIdeInt(route.Raw, "max", "limit", out var max))
            args["max"] = JsonSerializer.SerializeToElement(max);

        var newName = ExtractMcpKeyed(route.Raw, "new_name")
            ?? ExtractMcpKeyed(route.Raw, "name")
            ?? ExtractMcpKeyed(route.Raw, "to");
        if (newName is { Length: > 0 })
            args["new_name"] = JsonSerializer.SerializeToElement(newName);

        if (TryParseIdeInt(route.Raw, "action_index", "index", out var actionIndex)
            || TryParseIdeInt(route.Raw, "i", "i", out actionIndex))
            args["action_index"] = JsonSerializer.SerializeToElement(actionIndex);

        if (ExtractMcpKeyed(route.Raw, "apply") is { Length: > 0 } applyRaw
            && bool.TryParse(applyRaw, out var apply))
            args["apply"] = JsonSerializer.SerializeToElement(apply);

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

            if (root.TryGetProperty("completions", out var comps) && comps.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {comps.GetArrayLength()} item(s)");

            if (root.TryGetProperty("signatures", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {sigs.GetArrayLength()} sig(s)");

            if (root.TryGetProperty("symbols", out var syms) && syms.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {syms.GetArrayLength()} sym(s)");

            if (root.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {acts.GetArrayLength()} action(s)");

            if (root.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {nm.GetString()}");

            if (root.TryGetProperty("changes", out var ch) && ch.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {ch.GetArrayLength()} change(s)");

            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                return TruncPulse($"ide · {ShortIdeOp(op)} · {files.GetArrayLength()} file(s)");

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
            "get_completions" => "complete",
            "get_signature_help" => "signature",
            "get_document_symbols" => "symbols",
            "get_symbol_at_position" => "symbol",
            "rename_symbol" => "rename",
            "code_actions" => "actions",
            "apply_code_action" => "apply",
            _ => op
        };
}
