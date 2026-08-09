#nullable enable
using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;

namespace CdpMcp;

/// <summary>
/// Official MEAI <see cref="IChatClient"/> factories for Face Completions
/// (parity with CIDE <c>CascadeIdeMafChatClientFactories</c>).
/// </summary>
internal static class CitizenMafChatClientFactories
{
    /// <summary>OpenAI-compatible API base must end with <c>/v1</c> for the SDK.</summary>
    public static Uri NormalizeOpenAiCompatibleEndpoint(string baseUrl)
    {
        var t = (baseUrl ?? "").Trim().TrimEnd('/');
        if (t.Length == 0)
            t = "https://api.openai.com";
        if (!t.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            t += "/v1";
        return new Uri(t + "/", UriKind.Absolute);
    }

    public static IChatClient? CreateOpenAiCompatibleChatClientOrNull(
        string apiKey,
        string baseUrl,
        string modelId,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;
        var model = (modelId ?? "").Trim();
        if (model.Length == 0)
            return null;

        var cred = new ApiKeyCredential(apiKey.Trim());
        var opts = new OpenAI.OpenAIClientOptions
        {
            Endpoint = NormalizeOpenAiCompatibleEndpoint(baseUrl)
        };
        if (httpClient is not null)
            opts.Transport = new HttpClientPipelineTransport(httpClient);

        var root = new OpenAI.OpenAIClient(cred, opts);
        return root.GetChatClient(model).AsIChatClient();
    }

    public static IChatClient? CreateAnthropicChatClientOrNull(
        string apiKey,
        string modelId,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;
        var model = (modelId ?? "").Trim();
        if (model.Length == 0)
            return null;

        var native = httpClient is null
            ? new AnthropicClient { ApiKey = apiKey.Trim() }
            : new AnthropicClient { ApiKey = apiKey.Trim(), HttpClient = httpClient };
        return native.AsIChatClient(model);
    }
}
