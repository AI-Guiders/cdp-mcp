#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent pkg — sync MetaDispatch cdp_pkg_*; place pkg organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake pkg JSON; live uses MetaDispatchResolver("cdp_pkg_*", …).</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, string>? PkgDispatchOverride { get; set; }

    static Applied RunPkg(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "list" : route.Op!;
        var tool = MapPkgTool(op);
        var args = BuildPkgArgs(route, op);

        try
        {
            string json;
            if (PkgDispatchOverride is { } ov)
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

            var ok = TryReadLifecycleOk(json) || TryReadPkgOk(json);
            var pulse = TryReadPkgPulse(json, op, route.Tool, route.Scene);
            var seat = IdeDeskSeats.PlaceOrgan("pkg");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "pkg",
                Seat: seat,
                Go: "pkg",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "pkg_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "pkg",
                Go: "pkg",
                Path: route.Path,
                Reason: "pkg_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "pkg",
                Go: "pkg",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string MapPkgTool(string op) =>
        op switch
        {
            "find" => "cdp_pkg_find",
            "add" => "cdp_pkg_add",
            "remove" => "cdp_pkg_remove",
            "update" => "cdp_pkg_update",
            "outdated" => "cdp_pkg_outdated",
            "audit" => "cdp_pkg_audit",
            "latest" => "cdp_pkg_latest",
            "upgrade_plan" => "cdp_pkg_upgrade_plan",
            "fix_vuln" => "cdp_pkg_fix_vuln",
            "supply_chain" => "cdp_pkg_supply_chain",
            _ => "cdp_pkg_list"
        };

    static Dictionary<string, JsonElement> BuildPkgArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        PutIfPresent(args, "path", route.Path);

        if (op is "find")
        {
            PutIfPresent(args, "query", route.Scene);
            PutIntIfPresent(args, "take", route.Detail);
        }
        else if (op is "add" or "remove" or "update")
        {
            PutIfPresent(args, "id", route.Tool);
            if (op is "add" or "update")
                PutIfPresent(args, "version", route.Detail);
        }
        else if (op is "latest")
            PutIfPresent(args, "id", route.Tool);
        else if (op is "supply_chain")
            PutIfPresent(args, "root", route.Path);

        return args;
    }

    static bool TryReadPkgOk(string json)
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

    static string? TryReadPkgPulse(string json, string op, string? id, string? query)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            var bits = new List<string> { "pkg", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("summary", out var sum) && sum.ValueKind == JsonValueKind.String
                && sum.GetString() is { Length: > 0 } s)
                bits.Add(s);
            else if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            if (id is { Length: > 0 })
                bits.Add(id);
            else if (query is { Length: > 0 })
                bits.Add(query);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("pkg " + op);
        }
    }
}
