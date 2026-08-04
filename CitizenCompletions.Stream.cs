#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>SSE read + delta extractors (CIDE OpenAiCompatibleProvider parity).</summary>
internal static partial class CitizenCompletions
{
    static bool IsJsonNotEventStream(HttpResponseMessage resp)
    {
        var mt = resp.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mt))
            return false;
        if (mt.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            return false;
        return mt.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Read SSE <c>data:</c> lines; reset idle budget each line.
    /// Prefer accumulated <c>content</c>; else reasoning_* (GLM/Qwen).
    /// </summary>
    static OpenAiExtract ReadSseOpenAiAccumulated(HttpResponseMessage resp, CancellationToken ct)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void Touch() => idleCts.CancelAfter(IdleTimeout);
        Touch();

        using var stream = resp.Content.ReadAsStream(idleCts.Token);
        using var reader = new StreamReader(stream);
        var contentSb = new StringBuilder();
        var reasoningSb = new StringBuilder();
        string? finish = null;
        int? prompt = null, completion = null, total = null;

        while (true)
        {
            var line = reader.ReadLineAsync(idleCts.Token).AsTask().GetAwaiter().GetResult();
            if (line is null)
                break;
            Touch();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var json = line.Length > 5 ? line[5..].Trim() : "";
            if (json.Length == 0 || json == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    prompt ??= TryIntProp(usage, "prompt_tokens");
                    completion ??= TryIntProp(usage, "completion_tokens");
                    total ??= TryIntProp(usage, "total_tokens");
                }

                var (c, r, fr) = ExtractOpenAiDeltaParts(json);
                if (!string.IsNullOrEmpty(fr))
                    finish = fr;
                if (!string.IsNullOrEmpty(c))
                    contentSb.Append(c);
                if (!string.IsNullOrEmpty(r))
                    reasoningSb.Append(r);
            }
            catch (JsonException)
            {
                // skip malformed chunk
            }
        }

        if (contentSb.Length > 0)
            return new OpenAiExtract(contentSb.ToString(), finish, false, prompt, completion, total);
        if (reasoningSb.Length > 0)
            return new OpenAiExtract(reasoningSb.ToString(), finish, true, prompt, completion, total);
        return new OpenAiExtract(null, finish, false, prompt, completion, total);
    }

    /// <summary>
    /// Read SSE <c>data:</c> lines; reset idle budget each line.
    /// Returns accumulated text (may be null/empty). Anthropic path.
    /// </summary>
    static string? ReadSseAccumulated(
        HttpResponseMessage resp,
        Func<string, string?> extractDelta,
        CancellationToken ct)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void Touch() => idleCts.CancelAfter(IdleTimeout);
        Touch();

        using var stream = resp.Content.ReadAsStream(idleCts.Token);
        using var reader = new StreamReader(stream);
        var sb = new StringBuilder();
        while (true)
        {
            var line = reader.ReadLineAsync(idleCts.Token).AsTask().GetAwaiter().GetResult();
            if (line is null)
                break;
            Touch();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var json = line.Length > 5 ? line[5..].Trim() : "";
            if (json.Length == 0 || json == "[DONE]")
                continue;
            string? delta;
            try
            {
                delta = extractDelta(json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
                sb.Append(delta);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    static string? ExtractOpenAiDelta(string chunkJson)
    {
        var (content, reasoning, _) = ExtractOpenAiDeltaParts(chunkJson);
        if (!string.IsNullOrEmpty(content))
            return content;
        return string.IsNullOrEmpty(reasoning) ? null : reasoning;
    }

    static string? ExtractAnthropicDelta(string chunkJson)
    {
        using var doc = JsonDocument.Parse(chunkJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl)
            || typeEl.ValueKind != JsonValueKind.String)
            return null;
        var type = typeEl.GetString();
        if (type is not ("content_block_delta" or "content_block_start"))
            return null;

        if (root.TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("text", out var textEl)
            && textEl.ValueKind == JsonValueKind.String)
            return textEl.GetString();

        if (root.TryGetProperty("content_block", out var block)
            && block.TryGetProperty("text", out var blockText)
            && blockText.ValueKind == JsonValueKind.String)
            return blockText.GetString();

        return null;
    }

    static TurnResult FailTimeout(BuiltTurn built, Resolved resolved, string which) =>
        new(
            false,
            "timeout",
            which + " · Headers=" + (int)HeadersTimeout.TotalSeconds
                + "s Idle=" + (int)IdleTimeout.TotalSeconds
                + "s Overall=" + (int)OverallTimeout.TotalSeconds + "s",
            null,
            resolved.Model,
            resolved.Provider,
            built,
            null,
            null,
            false);

    /// <summary>
    /// Map OperationCanceledException: outer cancel → rethrow; budget → timeout result.
    /// </summary>
    static TurnResult MapCancel(
        BuiltTurn built,
        Resolved resolved,
        OperationCanceledException _,
        CancellationToken outer)
    {
        if (outer.IsCancellationRequested)
            throw new OperationCanceledException(outer);
        return FailTimeout(built, resolved, "http_budget");
    }
}
