#nullable enable
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CdpMcpBridge;

/// <summary>
/// Mirror of CdpMcp/CdpJson.cs (bridge does not reference the CdpMcp assembly —
/// same pattern as McpJsonRelax mirror copies). JSON serialization SSOT for the
/// bridge: non-ASCII stays as written, LLMs read this wire.
/// </summary>
internal static class CdpJson
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonWriterOptions WriterOptionsIndented => new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
