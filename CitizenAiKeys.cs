#nullable enable
using System.Text.Json;
using Tomlyn;

namespace CdpMcp;

/// <summary>
/// Citizen host keyring reader (CDP-ADR-0026). Same file as CIDE:
/// <c>%LocalAppData%\CascadeIDE\ai-keys.toml</c>. Never log raw key values.
/// OpenAI-compat (Cloud.ru FM / DeepSeek / OpenAI) uses <c>open_ai_*</c> + optional base_url/model.
/// </summary>
internal static class CitizenAiKeys
{
    public const string FileName = "ai-keys.toml";

    /// <summary>Default when <c>open_ai_api_key</c> is set but base_url omitted — Cloud.ru Foundation Models.</summary>
    public const string DefaultOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";

    /// <summary>Default Cloud.ru FM chat model when <c>open_ai_model</c> omitted.</summary>
    public const string DefaultOpenAiModel = "Qwen/Qwen3-Coder-Next";

    public sealed record Snapshot(
        string? AnthropicApiKey,
        string? OpenAiApiKey,
        string? DeepSeekApiKey,
        string? OpenAiBaseUrl,
        string? OpenAiModel,
        string Path,
        bool FileExists)
    {
        public bool HasAny =>
            !string.IsNullOrWhiteSpace(AnthropicApiKey)
            || !string.IsNullOrWhiteSpace(OpenAiApiKey)
            || !string.IsNullOrWhiteSpace(DeepSeekApiKey);

        public bool HasAnthropic => !string.IsNullOrWhiteSpace(AnthropicApiKey);
        public bool HasOpenAi => !string.IsNullOrWhiteSpace(OpenAiApiKey);

        /// <summary>Live invite: Anthropic Messages or OpenAI-compat chat.completions.</summary>
        public bool HasLiveProvider => HasAnthropic || HasOpenAi;

        public string ResolvedOpenAiBaseUrl =>
            string.IsNullOrWhiteSpace(OpenAiBaseUrl) ? DefaultOpenAiBaseUrl : OpenAiBaseUrl.Trim();

        public string ResolvedOpenAiModel =>
            string.IsNullOrWhiteSpace(OpenAiModel) ? DefaultOpenAiModel : OpenAiModel.Trim();

        /// <summary>Safe for tool results / pressure — never includes secrets.</summary>
        public object ToPublicPulse() => new
        {
            path = Path,
            file_exists = FileExists,
            anthropic = Masked(AnthropicApiKey),
            open_ai = Masked(OpenAiApiKey),
            deep_seek = Masked(DeepSeekApiKey),
            open_ai_base_url = ResolvedOpenAiBaseUrl,
            open_ai_model = ResolvedOpenAiModel,
            has_any = HasAny,
            has_live = HasLiveProvider
        };
    }

    sealed class AiKeysTomlDoc
    {
        public string? AnthropicApiKey { get; set; }
        public string? OpenAiApiKey { get; set; }
        public string? DeepSeekApiKey { get; set; }
        public string? OpenAiBaseUrl { get; set; }
        public string? OpenAiModel { get; set; }
    }

    static readonly TomlSerializerOptions SnakeOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadeIDE",
            FileName);

    public static Snapshot Load(string? pathOverride = null)
    {
        var path = string.IsNullOrWhiteSpace(pathOverride) ? DefaultPath : pathOverride;
        if (!File.Exists(path))
            return Empty(path, fileExists: false);

        try
        {
            var text = File.ReadAllText(path);
            return Parse(text, path);
        }
        catch
        {
            return Empty(path, fileExists: true);
        }
    }

    /// <summary>Public for tests — parse TOML body without touching disk.</summary>
    internal static Snapshot Parse(string toml, string pathForMeta)
    {
        if (string.IsNullOrWhiteSpace(toml))
            return Empty(pathForMeta, fileExists: true);

        var doc = TomlSerializer.Deserialize<AiKeysTomlDoc>(toml, SnakeOpts) ?? new AiKeysTomlDoc();
        return new Snapshot(
            Norm(doc.AnthropicApiKey),
            Norm(doc.OpenAiApiKey),
            Norm(doc.DeepSeekApiKey),
            Norm(doc.OpenAiBaseUrl),
            Norm(doc.OpenAiModel),
            pathForMeta,
            FileExists: true);
    }

    static Snapshot Empty(string path, bool fileExists) =>
        new(null, null, null, null, null, path, fileExists);

    static string? Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static string Masked(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "missing";
        if (key.Length <= 8)
            return "set";
        return "set…" + key[^4..];
    }
}
