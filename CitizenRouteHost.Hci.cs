#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent hci — sync CallAsync on codebase_index backend (Hybrid Index).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake backend JSON; live uses <see cref="ByDomainResolver"/>.</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, Task<string>>? HciCallOverride { get; set; }

    static Applied RunHci(CitizenIntentRouter.Route route)
    {
        var tool = string.IsNullOrWhiteSpace(route.Op) ? "status" : route.Op!;
        var underlying = ToHciUnderlying(tool);
        var args = BuildHciArgs(route, tool);

        try
        {
            string json;
            if (HciCallOverride is { } ov)
            {
                json = ov(underlying, args).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                if (!byDomain.TryGetValue(CdpDomains.CodebaseIndex, out var backend) || !backend.IsEnabled)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "hci",
                        Go: "hci",
                        Reason: "hci_backend_disabled");
                }

                IReadOnlyDictionary<string, JsonElement> callArgs = args;
                var session = SessionResolver?.Invoke();
                if (session is not null)
                    callArgs = CodebaseIndexSessionDefaults.WithSession(args, session);

                if (!HasNonEmpty(callArgs, "workspace_path")
                    && tool is not "version" and not "man")
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "hci",
                        Go: "hci",
                        Reason: "hci_workspace_required");
                }

                json = backend.CallAsync(underlying, callArgs)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadHciPulse(json, tool);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "hci",
                Go: "hci",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "hci_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "hci",
                Go: "hci",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string ToHciUnderlying(string tool) =>
        tool switch
        {
            "search" => "codebase_index_search",
            "status" => "codebase_index_status",
            "reindex" => "codebase_index_reindex",
            "explain" => "codebase_index_explain",
            "version" => "codebase_index_version",
            "man" => "man",
            _ => tool.StartsWith("codebase_index_", StringComparison.Ordinal)
                ? tool
                : "codebase_index_" + tool
        };

    static Dictionary<string, JsonElement> BuildHciArgs(CitizenIntentRouter.Route route, string tool)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var key in HciArgKeys)
        {
            var val = ExtractMcpKeyed(route.Raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        if (route.NewString is { Length: > 0 } query && !args.ContainsKey("query") && !args.ContainsKey("q"))
            args["query"] = JsonSerializer.SerializeToElement(query);

        if (route.Path is { Length: > 0 } path && !args.ContainsKey("workspace_path"))
            args["workspace_path"] = JsonSerializer.SerializeToElement(path);

        if (ExtractMcpKeyed(route.Raw, "workspace") is { Length: > 0 } ws && !args.ContainsKey("workspace_path"))
            args["workspace_path"] = JsonSerializer.SerializeToElement(ws);

        if (tool is "explain"
            && !args.ContainsKey("hit_id")
            && ExtractMcpKeyed(route.Raw, "id") is { Length: > 0 } id)
            args["hit_id"] = JsonSerializer.SerializeToElement(id);

        if (ExtractMcpKeyed(route.Raw, "full_rebuild") is { Length: > 0 } frRaw
            && bool.TryParse(frRaw, out var fullRebuild))
            args["full_rebuild"] = JsonSerializer.SerializeToElement(fullRebuild);

        if (ExtractMcpKeyed(route.Raw, "top_n") is { Length: > 0 } topRaw
            && int.TryParse(topRaw, out var topN))
            args["top_n"] = JsonSerializer.SerializeToElement(topN);

        if (ExtractMcpKeyed(route.Raw, "semantic") is { Length: > 0 } semRaw
            && bool.TryParse(semRaw, out var semantic))
            args["semantic"] = JsonSerializer.SerializeToElement(semantic);

        return args;
    }

    static readonly string[] HciArgKeys =
    [
        "workspace_path", "solution_path", "query", "q", "text", "pattern",
        "hit_id", "path_prefix", "top_n", "tool"
    ];

    static bool HasNonEmpty(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el)
        && el.ValueKind == JsonValueKind.String
        && el.GetString() is { Length: > 0 };

    static string? TryReadHciPulse(string json, string tool)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "hci", tool };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add(n + " hit(s)");
            else if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
                bits.Add(hits.GetArrayLength() + " hit(s)");
            if (root.TryGetProperty("missKind", out var miss) && miss.ValueKind == JsonValueKind.String
                && miss.GetString() is { Length: > 0 } mk)
                bits.Add(mk);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(TruncPulse(e) ?? e);
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                bits.Add(TruncPulse(pulse) ?? pulse);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("hci " + tool);
        }
    }
}
