#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent deploy — sync IdeDeploy.Run (soft/hard/rollout).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake deploy JSON; live uses IdeDeploy.Run.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? DeployCallOverride { get; set; }

    static Applied RunDeploy(CitizenIntentRouter.Route route)
    {
        var mode = string.IsNullOrWhiteSpace(route.Op) ? "hard" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && DeployCallOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "deploy",
                Go: "deploy",
                Reason: "no_session");
        }

        var args = BuildDeployArgs(route.Raw, mode, route.Detail);

        try
        {
            object result;
            if (DeployCallOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeDeploy.Run(session!, args);

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadDeployOk(json);
            var pulse = TryReadDeployPulse(json, mode);
            var seat = IdeDeskSeats.PlaceOrgan("deploy");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "deploy",
                Seat: seat,
                Go: "deploy",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "deploy_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "deploy",
                Go: "deploy",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildDeployArgs(string raw, string mode, string? target)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["mode"] = JsonSerializer.SerializeToElement(mode)
        };

        if (!string.IsNullOrWhiteSpace(target))
            args["target"] = JsonSerializer.SerializeToElement(target);

        foreach (var key in new[] { "script", "to" })
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 } && !args.ContainsKey(key == "to" ? "target" : key))
                args[key == "to" ? "target" : key] = JsonSerializer.SerializeToElement(val);
        }

        foreach (var key in new[] { "dry_run", "peek", "force", "use_nuget", "UseNuGet", "no_nudge", "NoNudgeMcp", "include_raw", "include_raw_output" })
        {
            if (ExtractMcpKeyed(raw, key) is { Length: > 0 } v
                && IsTruthyToken(v))
                args[key] = JsonSerializer.SerializeToElement(true);
        }

        // Positional dry_run without key= — "deploy dry_run" / "deploy mode=hard dry_run"
        if (!args.ContainsKey("dry_run") && !args.ContainsKey("peek")
            && raw.Contains("dry_run", StringComparison.OrdinalIgnoreCase)
            && ExtractMcpKeyed(raw, "dry_run") is null)
        {
            var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Any(t => t.Equals("dry_run", StringComparison.OrdinalIgnoreCase)
                || t.Equals("peek", StringComparison.OrdinalIgnoreCase)))
                args["dry_run"] = JsonSerializer.SerializeToElement(true);
        }

        return args;
    }

    static bool IsTruthyToken(string v) =>
        v.Equals("true", StringComparison.OrdinalIgnoreCase)
        || v.Equals("1", StringComparison.OrdinalIgnoreCase)
        || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || v.Equals("on", StringComparison.OrdinalIgnoreCase);

    static bool TryReadDeployOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            return root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadDeployPulse(string json, string mode)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("deploy " + pulse);

            var bits = new List<string> { mode };
            if (root.TryGetProperty("dry_run", out var dry) && dry.ValueKind == JsonValueKind.True)
                bits.Add("dry_run");
            if (root.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String
                && t.GetString() is { Length: > 0 } target)
                bits.Add(Path.GetFileName(target.TrimEnd('\\', '/')));
            return TruncPulse("deploy " + string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("deploy " + mode);
        }
    }
}
