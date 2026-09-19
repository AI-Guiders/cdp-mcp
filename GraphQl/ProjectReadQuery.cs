#nullable enable
using System.Text.Json;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier B project-read nested Query — create/close/add stay mutate MCP.</summary>
internal sealed class ProjectReadQuery
{
    readonly CdpGraphQlCall _call;

    public ProjectReadQuery(CdpGraphQlCall call) => _call = call;

    public Task<EngineEnvelopeNode> Scene(CancellationToken ct = default) =>
        SituationalReadResolvers.ProjectSceneAsync(_call, ct);

    public Task<EngineEnvelopeNode> Recent(CancellationToken ct = default) =>
        SituationalReadResolvers.RecentAsync(_call, ct);

    public Task<EngineEnvelopeNode> List(string? root = null, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(root))
            args["root"] = JsonSerializer.SerializeToElement(root);
        return DispatchAsync("cdp_project_list", "project_list/v0", args, ct);
    }

    async Task<EngineEnvelopeNode> DispatchAsync(
        string tool,
        string schema,
        Dictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (_call.DispatchToolAsync is null)
            throw new GraphQLException($"dispatch_unavailable: need DispatchToolAsync for {tool}");

        var json = await _call.DispatchToolAsync(tool, args, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ok = !(root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False);
        var sch = root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? schema
            : (root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                ? k.GetString() ?? schema
                : schema);
        var hint = root.TryGetProperty("hint", out var h) ? h.GetString()
            : root.TryGetProperty("error", out var e) ? e.GetString() : null;
        return new EngineEnvelopeNode(sch!, ok, json, hint);
    }
}
