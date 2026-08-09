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

    /// <summary>When a vision frame is present and model is blank/non-vision, prefer Qwen3.6 vision.</summary>
    static string? ResolveVisionModel(string? model, CitizenVisionLatch.Frame? vision)
    {
        if (vision is null)
            return model;
        if (string.IsNullOrWhiteSpace(model) || CitizenVisionLatch.ModelLooksNonVision(model))
            return CitizenVisionLatch.DefaultVisionModel;
        return model.Trim();
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
        CancellationToken cancellationToken) =>
        WithTransientRetry(() => TurnOpenAiCompatOnce(built, resolved, maxTokens, cancellationToken));

    static TurnResult TurnOpenAiCompatOnce(
        BuiltTurn built,
        Resolved resolved,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // TestChatClient = MEAI stream path. TestHandler = legacy HTTP stubs (SSE/reasoning shape).
        if (TestChatClient is not null)
            return TurnViaMeAi(built, resolved, TestChatClient, maxTokens, cancellationToken);
        if (TestHandler is not null)
            return TurnOpenAiCompatOnceHttp(built, resolved, maxTokens, cancellationToken);

        var client = CitizenMafChatClientFactories.CreateOpenAiCompatibleChatClientOrNull(
            resolved.ApiKey,
            resolved.BaseUrl ?? CitizenAiKeys.DefaultOpenAiBaseUrl,
            resolved.Model,
            Http);
        if (client is null)
            return new TurnResult(false, "no_client", "OpenAI-compat IChatClient factory returned null", null, resolved.Model, resolved.Provider, built, null, null, false);

        using (client as IDisposable)
            return TurnViaMeAi(built, resolved, client, maxTokens, cancellationToken);
    }

    static TurnResult TurnOpenAiCompatOnceHttp(
        BuiltTurn built,
        Resolved resolved,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        using var turnCts = CreateTurnCts(cancellationToken);
        var url = ChatCompletionsUrl(resolved.BaseUrl!);
        var oaiMessages = new List<object> { new { role = "system", content = built.System } };
        var lastUserIdx = -1;
        for (var i = 0; i < built.Messages.Count; i++)
        {
            if (built.Messages[i].Role == "user")
                lastUserIdx = i;
        }

        for (var i = 0; i < built.Messages.Count; i++)
        {
            var m = built.Messages[i];
            if (built.Vision is { } vision && i == lastUserIdx && m.Role == "user")
            {
                var dataUrl = "data:" + vision.Mime + ";base64," + Convert.ToBase64String(vision.Bytes);
                oaiMessages.Add(new
                {
                    role = m.Role,
                    content = new object[]
                    {
                        new { type = "text", text = m.Content },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                });
            }
            else
            {
                oaiMessages.Add(new { role = m.Role, content = m.Content });
            }
        }

        // Wire dogfood needs temp=0 for @intent fidelity; dialog peer needs room to reason.
        var temperature = built.Mode == CitizenTurnMode.Dialog ? 0.6 : 0.0;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = resolved.Model,
            ["max_tokens"] = Math.Clamp(maxTokens, 64, 8192),
            ["temperature"] = temperature,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["messages"] = oaiMessages
        };
        // Wire: prefer no hidden reasoning budget (GLM/Qwen OpenAI-compat forks).
        // Dialog keeps provider default so thinking models can reason into content/reasoning_*.
        // Vision turns: always disable thinking — otherwise content∅ while budget burns on reasoning.
        if (built.Mode != CitizenTurnMode.Dialog || built.Vision is not null)
        {
            payload["enable_thinking"] = false;
            payload["chat_template_kwargs"] = new { enable_thinking = false };
        }

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + resolved.ApiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(turnCts.Token);
            headersCts.CancelAfter(HeadersTimeoutFor(built.Mode));
            using var resp = Http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headersCts.Token)
                .GetAwaiter()
                .GetResult();

            if (!resp.IsSuccessStatusCode)
            {
                var errBody = resp.Content.ReadAsStringAsync(turnCts.Token).GetAwaiter().GetResult();
                return FailHttp(built, resolved, resp.StatusCode, errBody);
            }

            // Stub/tests + providers that ignore stream → full JSON body.
            if (IsJsonNotEventStream(resp))
            {
                var body = resp.Content.ReadAsStringAsync(turnCts.Token).GetAwaiter().GetResult();
                var extract = ExtractOpenAiCompletion(body);
                return FinishText(built, resolved, extract.Text, extract);
            }

            var streamed = ReadSseOpenAiAccumulated(resp, turnCts.Token);
            return FinishText(built, resolved, streamed.Text, streamed);
        }
        catch (OperationCanceledException oce)
        {
            return MapCancel(built, resolved, oce, cancellationToken, built.Mode);
        }
        catch (HttpRequestException ex)
        {
            return FailNetwork(built, resolved, ex);
        }
        catch (IOException ex)
        {
            return FailNetwork(built, resolved, ex);
        }
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
}
