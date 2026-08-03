#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent run — sync wait IdeSessionLifecycle.RunAsync (build/test triad; go=run place-only).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake run JSON; live uses IdeSessionLifecycle.RunAsync.</summary>
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, CancellationToken, string>? RunLifecycleOverride { get; set; }

    static Applied RunProject(CitizenIntentRouter.Route route)
    {
        var session = SessionResolver?.Invoke();
        if (session is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "run",
                Go: "run",
                Path: route.Path,
                Reason: "no_session");
        }

        var args = BuildRunArgs(route);
        var timeoutSec = 120;
        if (CitizenIntentRouter.ExtractKeyedValue(route.Raw, "timeout_seconds") is { Length: > 0 } ts
            && int.TryParse(ts, out var n)
            && n > 0)
            timeoutSec = Math.Min(n, 600);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            string json;
            if (RunLifecycleOverride is { } ov)
            {
                json = ov(session, args, cts.Token);
            }
            else
            {
                json = IdeSessionLifecycle.RunAsync(session, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadLifecyclePulse(json);
            var seat = IdeDeskSeats.PlaceOrgan("run");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "run",
                Seat: seat,
                Go: "run",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "run_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "run",
                Go: "run",
                Path: route.Path,
                Reason: "run_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "run",
                Go: "run",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildRunArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (route.Path is { Length: > 0 } path)
            args["path"] = JsonSerializer.SerializeToElement(path);

        if (CitizenIntentRouter.ExtractKeyedValue(route.Raw, "configuration") is { Length: > 0 } cfg)
            args["configuration"] = JsonSerializer.SerializeToElement(cfg);
        else if (CitizenIntentRouter.ExtractKeyedValue(route.Raw, "config") is { Length: > 0 } config)
            args["configuration"] = JsonSerializer.SerializeToElement(config);

        var noBuildRaw = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "no_build")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "nobuild");
        if (noBuildRaw is not null
            && (noBuildRaw.Length == 0
                || noBuildRaw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || noBuildRaw.Equals("1", StringComparison.OrdinalIgnoreCase)))
            args["no_build"] = JsonSerializer.SerializeToElement(true);

        if (CitizenIntentRouter.ExtractKeyedValue(route.Raw, "timeout_seconds") is { Length: > 0 } timeout
            && int.TryParse(timeout, out var sec))
            args["timeout_seconds"] = JsonSerializer.SerializeToElement(sec);

        return args;
    }
}
