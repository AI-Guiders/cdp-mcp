#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent arch — sync IdeArchBoardChannel; place arch_desk organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake arch JSON; live uses <see cref="IdeArchBoardChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? ArchHandleOverride { get; set; }

    static Applied RunArch(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && ArchHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "arch",
                Go: "arch_desk",
                Reason: "no_session");
        }

        var args = BuildArchArgs(route, op);

        try
        {
            object result;
            if (ArchHandleOverride is { } ov)
                result = ov(session ?? new SessionContext(), args);
            else
                result = IdeArchBoardChannel.HandleJson(session!, args);

            var json = result is string s
                ? s
                : JsonSerializer.Serialize(result);
            var ok = TryReadArchOk(json);
            var pulse = TryReadArchPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("arch_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "arch",
                Seat: seat,
                Go: "arch_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "arch_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "arch",
                Go: "arch_desk",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildArchArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in ArchArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));

        // Path fills role/id/profile/candidate when keyed value absent.
        if (route.Path is { Length: > 0 } path)
        {
            if (op is "add_role" or "elect" or "reject" or "promote" or "add_candidates")
            {
                if (!args.ContainsKey("role") && !args.ContainsKey("role_id") && !args.ContainsKey("id"))
                    PutIfPresent(args, "role", path);
            }
            else if (op is "as_built")
            {
                if (!args.ContainsKey("profile"))
                    PutIfPresent(args, "profile", path);
            }
        }

        return args;
    }

    static readonly string[] ArchArgKeys =
    [
        "role", "kind", "id", "role_id", "note", "why",
        "anchors", "candidates", "candidate", "items", "candidate_id", "anchor", "label",
        "from", "from_role", "from_id", "to", "to_role", "to_id", "edge",
        "profile", "view", "board", "mode", "focus"
    ];

    static bool TryReadArchOk(string json)
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
            return root.TryGetProperty("pulse", out _) || root.TryGetProperty("roles", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadArchPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("arch " + op + " " + pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse("arch " + op + " · " + e);

            return TruncPulse("arch " + op);
        }
        catch
        {
            return TruncPulse("arch " + op);
        }
    }
}
