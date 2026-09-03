using AIGuiders.Platform.Execution.Language;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Buffer-plane language id SSOT: federation <see cref="LanguagePathRules"/> first,
/// then host-only extensions (xml, markdown, …).
/// </summary>
internal static class BufferLanguageRules
{
    public static string GuessLanguage(string path)
    {
        if (LanguagePathRules.ResolveLanguageId(path) is { } federationId)
            return federationId;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".csx" => CdpLanguages.Csharp,
            ".csproj" or ".props" or ".targets" or ".xml" or ".config" or ".xaml" => "xml",
            ".md" or ".markdown" => "markdown",
            ".toml" => "toml",
            ".json" or ".jsonc" => "json",
            _ => "text",
        };
    }

    /// <summary>Prefer path-derived id when buffer still has generic <c>text</c>.</summary>
    public static string Resolve(string path, string? bufferLanguage)
    {
        var fromPath = GuessLanguage(path);

        if (string.Equals(bufferLanguage, "text", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fromPath, "text", StringComparison.OrdinalIgnoreCase))
            return fromPath;

        if (!string.IsNullOrWhiteSpace(bufferLanguage))
            return bufferLanguage;

        return fromPath;
    }

    public static bool IsLrcLanguage(string? languageId) =>
        !string.IsNullOrWhiteSpace(languageId)
        && (languageId.Equals(CdpLanguages.Fsharp, StringComparison.OrdinalIgnoreCase)
            || languageId.Equals(CdpLanguages.Gdl, StringComparison.OrdinalIgnoreCase));

    public static bool SupportsOnlineBufferDiagnostics(string language) =>
        language.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase)
        || language.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase)
        || IsLrcLanguage(language);
}
