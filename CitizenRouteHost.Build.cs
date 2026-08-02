#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent build — sync wait IdeSessionLifecycle.BuildAsync (organ parity; not cockpit W-spray).</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext?>? SessionResolver { get; set; }
    internal static Func<ICdpBackendModule?>? BuildModuleResolver { get; set; }

    /// <summary>Tests / remount isolation.</summary>
    internal static void UnbindLifecycle()
    {
        SessionResolver = null;
        BuildModuleResolver = null;
    }

    static Applied RunBuild(CitizenIntentRouter.Route route)
    {
        var session = SessionResolver?.Invoke();
        if (session is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "build",
                Go: "build",
                Path: route.Path,
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (route.Path is { Length: > 0 } path)
            args["path"] = JsonSerializer.SerializeToElement(path);

        var buildMod = BuildModuleResolver?.Invoke();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var json = IdeSessionLifecycle.BuildAsync(session, args, buildMod, cts.Token)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadLifecyclePulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("build");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "build",
                Seat: seat,
                Go: "build",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "build_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "build",
                Go: "build",
                Path: route.Path,
                Reason: "build_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "build",
                Go: "build",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static bool TryReadLifecycleOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return n == 0;
            if (root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var errors))
                return errors == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadLifecycleError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.String
                && e.GetString() is { Length: > 0 } err)
                return TruncPulse(err);
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    static string? TryReadLifecyclePulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True ? "ok" : "fail";
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return "exit=" + n;
            return TruncPulse(json);
        }
        catch
        {
            return TruncPulse(json);
        }
    }

    static string? TruncPulse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        s = s.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return s.Length <= 120 ? s : s[..117] + "…";
    }
}
