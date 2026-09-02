namespace CdpMcp;

internal static partial class QualityGates
{
    /// <summary>
    /// <c>file_lines</c> / <c>suggest_sniper</c> apply to code buffers only — not prose/math/config.
    /// Disk scan already limits to <c>*.cs</c>; open-buffer eval must match that intent.
    /// </summary>
    internal static bool IsFileLinesSubject(DocBuffer buf) =>
        IsFileLinesSubject(buf.Path, buf.Language);

    internal static bool IsFileLinesSubject(string path, string? language)
    {
        var ext = Path.GetExtension(path);
        if (IsProseOrConfigExtension(ext))
            return false;
        if (IsCodeExtension(ext))
            return true;

        if (string.IsNullOrWhiteSpace(language))
            return false;

        var lang = language.Trim();
        if (IsProseOrConfigLanguage(lang))
            return false;
        return IsCodeLanguage(lang);
    }

    static bool IsCodeExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".cs" or ".csx" or ".fs" or ".fsx" or ".fsi" or ".vb"
            or ".ts" or ".tsx" or ".js" or ".jsx" or ".mjs" or ".cjs"
            or ".py" or ".pyw" or ".ps1" or ".psm1"
            or ".go" or ".rs" or ".java" or ".kt" or ".kts"
            or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp"
            or ".sql" or ".razor" or ".cshtml" => true,
        _ => false
    };

    static bool IsProseOrConfigExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".md" or ".markdown" or ".mdx" or ".txt" or ".adoc" or ".rst" or ".tex"
            or ".json" or ".jsonc" or ".toml" or ".yaml" or ".yml"
            or ".xml" or ".html" or ".htm" or ".css" or ".scss" or ".less"
            or ".svg" or ".csv" or ".log" or ".mdc" or ".editorconfig" => true,
        _ => false
    };

    static bool IsCodeLanguage(string lang) => lang.ToLowerInvariant() switch
    {
        "csharp" or "fsharp" or "typescript" or "javascript" or "python"
            or "powershell" or "go" or "rust" or "vb" or "java" or "kotlin" => true,
        _ => false
    };

    static bool IsProseOrConfigLanguage(string lang) => lang.ToLowerInvariant() switch
    {
        "markdown" or "text" or "toml" or "json" or "xml" or "yaml" or "html" or "css" => true,
        _ => false
    };
}
