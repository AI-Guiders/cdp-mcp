#nullable enable
using System.Text;

namespace CdpMcp;
internal static partial class CitizenCompletions
{
    static TurnResult FailHttp(BuiltTurn built, Resolved resolved, System.Net.HttpStatusCode code, string body)
    {
        var error = "http_" + (int)code;
        CitizenCostLedger.Record(
            built,
            resolved.Model,
            resolved.Provider,
            ok: false,
            error: error,
            promptTokens: null,
            completionTokens: null,
            totalTokens: null);
        return new(false, error, Trunc(body, 240), null, resolved.Model, resolved.Provider, built, null, null, false);
    }

    static TurnResult FinishText(BuiltTurn built, Resolved resolved, string? text, OpenAiExtract? meta = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            var hint = new StringBuilder("provider returned no text");
            if (meta is { } m)
            {
                if (!string.IsNullOrWhiteSpace(m.FinishReason))
                    hint.Append(" · finish_reason=").Append(m.FinishReason);
                if (m.CompletionTokens is int ct)
                    hint.Append(" · completion_tokens=").Append(ct);
                if (m.PromptTokens is int pt)
                    hint.Append(" · prompt_tokens=").Append(pt);
                if (string.Equals(m.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                    hint.Append(" · truncated — raise max_tokens or shorten prompt/reasoning");
            }

            CitizenCostLedger.Record(
                built,
                resolved.Model,
                resolved.Provider,
                ok: false,
                error: "empty_text",
                promptTokens: meta?.PromptTokens,
                completionTokens: meta?.CompletionTokens,
                totalTokens: meta?.TotalTokens);

            return new TurnResult(false, "empty_text", hint.ToString(), null, resolved.Model, resolved.Provider, built, null, null, false);
        }

        var intents = CitizenWireParser.Parse(text);
        var routes = CitizenIntentRouter.RouteAll(intents);
        var okRoutes = routes.Count(r => r.Ok);
        var okHint = routes.Count > 0
            ? $"ok — {okRoutes}/{routes.Count} intent routes"
            : intents.Count > 0
                ? "ok — wire parsed; no @intent lines to route"
                : "ok — reply has no @frame/@intent/@event lines";
        if (meta is { FromReasoning: true })
            okHint += " · text from reasoning";
        if (meta is { CompletionTokens: int used })
            okHint += " · completion_tokens=" + used;
        if (meta is { PromptTokens: int ptOk })
            okHint += " · prompt_tokens=" + ptOk;

        CitizenCostLedger.Record(
            built,
            resolved.Model,
            resolved.Provider,
            ok: true,
            error: null,
            promptTokens: meta?.PromptTokens,
            completionTokens: meta?.CompletionTokens,
            totalTokens: meta?.TotalTokens);

        return new TurnResult(true, null, okHint, text, resolved.Model, resolved.Provider, built, intents, routes, false);
    }

    static string? Trunc(string? s, int max)
    {
        if (s is null)
            return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
