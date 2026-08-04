#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// OpenAI-compat message extract — reasoning models (GLM/Qwen) often put answer in
/// <c>reasoning</c>/<c>reasoning_content</c> while <c>content</c> is empty.
/// ATL: reasoning models ≠ content-only.
/// </summary>
internal static partial class CitizenCompletions
{
    internal readonly record struct OpenAiExtract(
        string? Text,
        string? FinishReason,
        bool FromReasoning,
        int? PromptTokens,
        int? CompletionTokens,
        int? TotalTokens);

    /// <summary>Prefer non-empty content; else reasoning_content / reasoning / thinking.</summary>
    internal static string? CoalesceAssistantText(JsonElement messageOrDelta)
    {
        var content = TryStringProp(messageOrDelta, "content");
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        foreach (var key in new[] { "reasoning_content", "reasoning", "thinking", "reasoning_text" })
        {
            var r = TryStringProp(messageOrDelta, key);
            if (!string.IsNullOrWhiteSpace(r))
                return r;
        }

        return null;
    }

    internal static bool LooksLikeReasoningProp(string name) =>
        name is "reasoning_content" or "reasoning" or "thinking" or "reasoning_text";

    /// <summary>Full JSON body (non-stream) → text + finish_reason + usage.</summary>
    internal static OpenAiExtract ExtractOpenAiCompletion(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string? finish = null;
        string? text = null;
        var fromReasoning = false;

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            finish = TryStringProp(first, "finish_reason") ?? TryStringProp(first, "native_finish_reason");

            if (first.TryGetProperty("message", out var message))
            {
                var content = TryStringProp(message, "content");
                if (!string.IsNullOrWhiteSpace(content))
                {
                    text = content;
                }
                else
                {
                    text = CoalesceAssistantText(message);
                    fromReasoning = !string.IsNullOrWhiteSpace(text);
                }
            }
            else if (first.TryGetProperty("text", out var textEl)
                     && textEl.ValueKind == JsonValueKind.String)
            {
                text = textEl.GetString();
            }
        }

        int? prompt = null, completion = null, total = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            prompt = TryIntProp(usage, "prompt_tokens");
            completion = TryIntProp(usage, "completion_tokens");
            total = TryIntProp(usage, "total_tokens");
            // Some providers split reasoning into completion_tokens_details.reasoning_tokens — still in completion_tokens.
        }

        return new OpenAiExtract(text, finish, fromReasoning, prompt, completion, total);
    }

    /// <summary>SSE delta chunk → content preferred; reasoning only when content absent on this delta.</summary>
    internal static (string? Content, string? Reasoning, string? FinishReason) ExtractOpenAiDeltaParts(string chunkJson)
    {
        using var doc = JsonDocument.Parse(chunkJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
            return (null, null, null);

        var first = choices[0];
        var finish = TryStringProp(first, "finish_reason");
        if (!first.TryGetProperty("delta", out var delta))
            return (null, null, finish);

        var content = TryStringProp(delta, "content");
        string? reasoning = null;
        foreach (var key in new[] { "reasoning_content", "reasoning", "thinking", "reasoning_text" })
        {
            reasoning = TryStringProp(delta, key);
            if (!string.IsNullOrEmpty(reasoning))
                break;
        }

        return (content, reasoning, finish);
    }

    static string? TryStringProp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String)
            return p.GetString();
        if (p.ValueKind == JsonValueKind.Null)
            return null;
        // Rare: content as array of parts — take first text
        if (p.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in p.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    var s = part.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
                if (part.ValueKind == JsonValueKind.Object)
                {
                    var t = TryStringProp(part, "text") ?? TryStringProp(part, "content");
                    if (!string.IsNullOrWhiteSpace(t))
                        return t;
                }
            }
        }

        return null;
    }

    static int? TryIntProp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        return null;
    }
}
