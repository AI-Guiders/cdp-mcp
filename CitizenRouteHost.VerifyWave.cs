#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent verify_wave — sync IdeVerifyWaveChannel; place organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? VerifyWaveHandleOverride { get; set; }

    static Applied RunVerifyWave(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && VerifyWaveHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "verify_wave",
                Go: "verify_wave",
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        try
        {
            object result = VerifyWaveHandleOverride is { } ov
                ? ov(session ?? new SessionContext(), args)
                : IdeVerifyWaveChannel.HandleJson(session!, args);
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = !json.Contains("\"ok\": false", StringComparison.OrdinalIgnoreCase);
            var seat = IdeDeskSeats.PlaceOrgan("verify_wave");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "verify_wave",
                Seat: seat,
                Go: "verify_wave",
                Pulse: TruncPulse("verify_wave " + op),
                Reason: ok ? null : "verify_wave_failed");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "verify_wave",
                Go: "verify_wave",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }
}
