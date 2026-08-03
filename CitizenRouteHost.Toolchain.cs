#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent toolchain — sync IdeToolchainChannel; place organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake toolchain JSON; live uses <see cref="IdeToolchainChannel.HandleJson"/>.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, string>? ToolchainHandleOverride { get; set; }

    static Applied RunToolchain(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && ToolchainHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "toolchain",
                Go: "toolchain",
                Reason: "no_session");
        }

        var args = BuildToolchainArgs(route, op);

        try
        {
            var json = ToolchainHandleOverride is { } ov
                ? ov(session ?? new SessionContext(), args)
                : IdeToolchainChannel.HandleJson(session!, args);
            var ok = TryReadToolchainOk(json);
            var pulse = TryReadToolchainPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("toolchain");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "toolchain",
                Seat: seat,
                Go: "toolchain",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "toolchain_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "toolchain",
                Go: "toolchain",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildToolchainArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutIfPresent(args, "id",
            route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "id")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "toolchain")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "lang"));
        PutIfPresent(args, "via",
            route.Detail
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "via")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "manager"));
        PutIfPresent(args, "bins",
            route.Command
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "bins")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "bin"));
        PutIfPresent(args, "search_q", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "search_q"));
        PutIfPresent(args, "pairs_lsp", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "pairs_lsp"));
        PutIfPresent(args, "label", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "label"));
        PutIfPresent(args, "argv", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "argv"));

        return args;
    }

    static bool TryReadToolchainOk(string json)
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
            return root.TryGetProperty("op", out _) || root.TryGetProperty("toolchains", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadToolchainPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("toolchain " + op + " " + pulse);

            var bits = new List<string> { "toolchain", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                && idEl.GetString() is { Length: > 0 } id)
                bits.Add("id=" + id);
            if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                && st.GetString() is { Length: > 0 } status)
                bits.Add(status);
            if (root.TryGetProperty("count", out var count) && count.TryGetInt32(out var n))
                bits.Add("n=" + n);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);
            if (root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                && reason.GetString() is { Length: > 0 } r)
                bits.Add(r);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("toolchain " + op);
        }
    }
}
