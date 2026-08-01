#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>OpenAI-compatible chat.completions peel (Cloud.ru FM / OpenAI / DeepSeek).</summary>
internal static partial class CitizenCompletions
{
    /// <summary>
    /// Prefer OpenAI-compat when open_ai key present (Cloud.ru FM dogfood);
    /// else Anthropic. TestApiKey forces Anthropic; TestOpenAiApiKey forces OAI.
    /// </summary>
    /// <summary>Dry-run model label mirrors live <see cref="ResolveProvider"/> (FM-first when keys empty).</summary>
    static string ResolveDryRunModel(string? model)
    {
        var resolved = ResolveProvider(CitizenAiKeys.Load(), model);
        if (resolved is not null)
            return resolved.Model;
        if (!string.IsNullOrWhiteSpace(model))
            return model.Trim();
        return CitizenAiKeys.DefaultOpenAiModel;
    }

    static Resolved? ResolveProvider(CitizenAiKeys.Snapshot keys, string? model)
    {
        if (!string.IsNullOrWhiteSpace(TestApiKey))
        {
            return new Resolved(
                ProviderAnthropic,
                TestApiKey.Trim(),
                string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
                null);
        }

        if (!string.IsNullOrWhiteSpace(TestOpenAiApiKey))
        {
            var baseUrl = string.IsNullOrWhiteSpace(TestOpenAiBaseUrl)
                ? CitizenAiKeys.DefaultOpenAiBaseUrl
                : TestOpenAiBaseUrl.Trim();
            var useModel = string.IsNullOrWhiteSpace(model)
                ? CitizenAiKeys.DefaultOpenAiModel
                : model.Trim();
            return new Resolved(ProviderOpenAiCompat, TestOpenAiApiKey.Trim(), useModel, baseUrl);
        }

        if (keys.HasOpenAi)
        {
            var useModel = string.IsNullOrWhiteSpace(model)
                ? keys.ResolvedOpenAiModel
                : model.Trim();
            return new Resolved(
                ProviderOpenAiCompat,
                keys.OpenAiApiKey!,
                useModel,
                keys.ResolvedOpenAiBaseUrl);
        }

        if (keys.HasAnthropic)
        {
            return new Resolved(
                ProviderAnthropic,
                keys.AnthropicApiKey!,
                string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
                null);
        }

        return null;
    }

    static TurnResult TurnOpenAiCompat(
        BuiltTurn built,
        Resolved resolved,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var url = ChatCompletionsUrl(resolved.BaseUrl!);
        var oaiMessages = new List<object> { new { role = "system", content = built.System } };
        foreach (var m in built.Messages)
            oaiMessages.Add(new { role = m.Role, content = m.Content });

        var payload = new Dictionary<string, object?>
        {
            ["model"] = resolved.Model,
            ["max_tokens"] = Math.Clamp(maxTokens, 64, 8192),
            ["stream"] = false,
            ["messages"] = oaiMessages
        };
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + resolved.ApiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = Http.SendAsync(req, cancellationToken).GetAwaiter().GetResult();
        var body = resp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
            return FailHttp(built, resolved, resp.StatusCode, body);

        return FinishText(built, resolved, ExtractOpenAiText(body));
    }

    /// <summary>Normalize base like CIDE OpenAiCompatibleProvider; append /chat/completions.</summary>
    internal static string ChatCompletionsUrl(string? baseUrl)
    {
        var t = (baseUrl ?? "").Trim().TrimEnd('/');
        if (t.Length == 0)
            t = CitizenAiKeys.DefaultOpenAiBaseUrl.TrimEnd('/');
        if (!t.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            t += "/v1";
        return t + "/chat/completions";
    }

    static string? ExtractOpenAiText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
            return null;

        var first = choices[0];
        if (first.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (first.TryGetProperty("text", out var textEl)
            && textEl.ValueKind == JsonValueKind.String)
            return textEl.GetString();

        return null;
    }
}
