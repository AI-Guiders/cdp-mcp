#nullable enable
using System.Text.Json;
using Tomlyn;

namespace CdpMcp;

/// <summary>
/// Citizen host keyring reader (CDP-ADR-0026). Same file as CIDE:
/// <c>%LocalAppData%\CascadeIDE\ai-keys.toml</c>. Never log raw key values.
/// </summary>
internal static class CitizenAiKeys
{
    public const string FileName = "ai-keys.toml";

    public sealed record Snapshot(
        string? AnthropicApiKey,
        string? OpenAiApiKey,
        string? DeepSeekApiKey,
        string Path,
        bool FileExists)
    {
        public bool HasAny =>
            !string.IsNullOrWhiteSpace(AnthropicApiKey)
            || !string.IsNullOrWhiteSpace(OpenAiApiKey)
            || !string.IsNullOrWhiteSpace(DeepSeekApiKey);

        /// <summary>Safe for tool results / pressure — never includes secrets.</summary>
        public object ToPublicPulse() => new
        {
            path = Path,
            file_exists = FileExists,
            anthropic = Masked(AnthropicApiKey),
            open_ai = Masked(OpenAiApiKey),
            deep_seek = Masked(DeepSeekApiKey),
            has_any = HasAny
        };
    }

    sealed class AiKeysTomlDoc
    {
        public string? AnthropicApiKey { get; set; }
        public string? OpenAiApiKey { get; set; }
        public string? DeepSeekApiKey { get; set; }
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
            return new Snapshot(null, null, null, path, FileExists: false);

        try
        {
            var text = File.ReadAllText(path);
            return Parse(text, path);
        }
        catch
        {
            return new Snapshot(null, null, null, path, FileExists: true);
        }
    }

    /// <summary>Public for tests — parse TOML body without touching disk.</summary>
    internal static Snapshot Parse(string toml, string pathForMeta)
    {
        if (string.IsNullOrWhiteSpace(toml))
            return new Snapshot(null, null, null, pathForMeta, FileExists: true);

        var doc = TomlSerializer.Deserialize<AiKeysTomlDoc>(toml, SnakeOpts) ?? new AiKeysTomlDoc();
        return new Snapshot(
            Norm(doc.AnthropicApiKey),
            Norm(doc.OpenAiApiKey),
            Norm(doc.DeepSeekApiKey),
            pathForMeta,
            FileExists: true);
    }

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
