#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;
internal static partial class CitizenCompletions
{
    static TurnResult FailHttp(BuiltTurn built, Resolved resolved, System.Net.HttpStatusCode code, string body) => new(false, "http_" + (int)code, Trunc(body, 240), null, resolved.Model, resolved.Provider, built, null, null, false);
    static TurnResult FinishText(BuiltTurn built, Resolved resolved, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TurnResult(false, "empty_text", "provider returned no text", null, resolved.Model, resolved.Provider, built, null, null, false);
        }

        var intents = CitizenWireParser.Parse(text);
        var routes = CitizenIntentRouter.RouteAll(intents);
        var okRoutes = routes.Count(r => r.Ok);
        return new TurnResult(true, null, routes.Count > 0 ? $"ok — {okRoutes}/{routes.Count} intent routes" : intents.Count > 0 ? "ok — wire parsed; no @intent lines to route" : "ok — reply has no @frame/@intent/@event lines", text, resolved.Model, resolved.Provider, built, intents, routes, false);
    }

    static string? Trunc(string? s, int max)
    {
        if (s is null)
            return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}