#nullable enable

namespace CdpMcp.GraphQl;

/// <summary>Slim engine JSON envelope for scene/pulse reads (git/test/semantic/pkg) — ADR-0233.</summary>
public sealed class EngineEnvelopeNode
{
    public EngineEnvelopeNode(string schema, bool ok, string rawJson, string? hint = null)
    {
        Schema = schema;
        Ok = ok;
        RawJson = rawJson;
        Hint = hint;
    }

    public string Schema { get; }
    public bool Ok { get; }
    public string RawJson { get; }
    public string? Hint { get; }
}
