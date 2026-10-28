using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CdpMcpBridge;

/// <summary>
/// ModelContextProtocol SDK serializes the stdio wire via
/// <c>JsonSerializer.SerializeToUtf8Bytes(msg, McpJsonUtilities.JsonContext.Default.JsonRpcMessage)</c>
/// (StreamServerTransport, same in v1.3.0–v2.3.0), whose source-generated options carry the
/// conservative escaping encoder — every non-ASCII char goes out as \uXXXX, ×2–3 tokens on
/// Cyrillic/CJK payloads that LLMs (not HTML embedders) read. Upstream keeps it "by design"
/// (modelcontextprotocol/csharp-sdk#626): this patch unfreezes both the JsonContext options
/// (the wire path) and McpJsonUtilities.DefaultOptions, then installs the relaxed encoder.
/// Mirror copy: guiders-core/src/AgentNotes.Core/McpJsonRelax.cs (bridge does not reference
/// AgentNotes.Core). Remove both when upstream ships a knob.
/// </summary>
public static class McpJsonRelax
{
    public static bool EnsureRelaxed()
    {
        try
        {
            var relaxed = Relax(DefaultOptions()) && Relax(WireOptions());

            // Self-test on the very options the stdio transport writes with.
            var wire = WireOptions();
            return relaxed
                   && wire is not null
                   && !JsonSerializer.Serialize("привет", wire).Contains("\\u");
        }
        catch
        {
            return false;
        }
    }

    private static bool Relax(JsonSerializerOptions? options)
    {
        if (options is null)
            return false;

        typeof(JsonSerializerOptions)
            .GetField("_isReadOnly", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(options, false);
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        return true;
    }

    /// <summary>McpJsonUtilities.DefaultOptions (public, frozen at construction).</summary>
    private static JsonSerializerOptions? DefaultOptions() =>
        Type.GetType("ModelContextProtocol.McpJsonUtilities, ModelContextProtocol.Core")
            ?.GetProperty("DefaultOptions")?.GetValue(null) as JsonSerializerOptions;

    /// <summary>Internal source-gen context: McpJsonUtilities.JsonContext.Default.Options —
    /// the instance the transports actually write with.</summary>
    private static JsonSerializerOptions? WireOptions()
    {
        var context = Type.GetType("ModelContextProtocol.McpJsonUtilities+JsonContext, ModelContextProtocol.Core")
            ?.GetProperty("Default")?.GetValue(null);
        return context?.GetType().GetProperty("Options")?.GetValue(context) as JsonSerializerOptions;
    }
}
