#nullable enable
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// JSON serialization SSOT: every <c>JsonSerializerOptions</c> instance and every
/// <c>Utf8JsonWriter</c> in this solution must come from here (conformance-scanned
/// by CdpJsonSweepTests). Payloads are read by LLMs, not HTML embedders — non-ASCII
/// (Cyrillic, arrows, CJK) stays as written instead of \uXXXX. Content-side twin of
/// <see cref="CdpMcpBridge.McpJsonRelax"/> (which relaxes the MCP wire envelope).
/// </summary>
internal static class CdpJson
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonWriterOptions WriterOptionsIndented => new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonWriterOptions WriterOptions => new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value, bool indented = false) =>
        JsonSerializer.Serialize(value, indented ? Indented : Compact);

    /// <summary>Serialize with caller-shaped options, forcing the SSOT encoder
    /// (naming/casing/custom converters from <paramref name="options"/> are kept).</summary>
    public static string Serialize<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, WithRelaxedEncoder(options));

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(json, options is null ? Compact : WithRelaxedEncoder(options));

    /// <summary>Clone of caller options with the SSOT encoder installed.</summary>
    public static JsonSerializerOptions WithRelaxedEncoder(JsonSerializerOptions options) => new(options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
