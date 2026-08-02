#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent test — sync wait IdeSessionLifecycle.TestAsync (organ parity with build).</summary>
internal static partial class CitizenRouteHost
{
    static Applied RunTest(CitizenIntentRouter.Route route)
    {
        var session = SessionResolver?.Invoke();
        if (session is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test",
                Go: "test",
                Path: route.Path,
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (route.Path is { Length: > 0 } path)
            args["path"] = JsonSerializer.SerializeToElement(path);
        if (ExtractFilter(route.Raw) is { Length: > 0 } filter)
            args["filter"] = JsonSerializer.SerializeToElement(filter);

        var buildMod = BuildModuleResolver?.Invoke();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var json = IdeSessionLifecycle.TestAsync(session, args, buildMod, cts.Token)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadLifecyclePulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("test");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "test",
                Seat: seat,
                Go: "test",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "test_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test",
                Go: "test",
                Path: route.Path,
                Reason: "test_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "test",
                Go: "test",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string? ExtractFilter(string raw)
    {
        const string key = "filter=";
        var idx = raw.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var rest = raw[(idx + key.Length)..].Trim();
        if (rest.Length == 0)
            return null;
        if (rest[0] is '"' or '\'')
        {
            var q = rest[0];
            var end = rest.IndexOf(q, 1);
            if (end > 1)
                return rest[1..end];
        }

        var space = rest.IndexOf(' ');
        return space > 0 ? rest[..space].Trim() : rest;
    }
}
