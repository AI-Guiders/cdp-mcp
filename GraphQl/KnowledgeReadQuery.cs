#nullable enable
using System.Text.Json;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier D knowledge nested Query — write/delete stay memory mutate MCP.</summary>
internal sealed class KnowledgeReadQuery
{
    readonly CdpGraphQlCall _call;

    public KnowledgeReadQuery(CdpGraphQlCall call) => _call = call;

    public async Task<KnowledgeRecallResult> Recall(
        string query,
        string? layer = null,
        int first = 15,
        CancellationToken ct = default) =>
        await new CdpQueryRoot().Knowledge(_call, query, layer, first, ct).ConfigureAwait(false);

    public Task<EngineEnvelopeNode> Tags(string? query = null, string mode = "inventory", CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["mode"] = JsonSerializer.SerializeToElement(mode),
        };
        if (!string.IsNullOrWhiteSpace(query))
            args["query"] = JsonSerializer.SerializeToElement(query);
        return DispatchAsync("memory_world_knowledge_tags", "knowledge_tags/v0", args, ct);
    }

    public Task<EngineEnvelopeNode> Read(string path, string mode = "full", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["mode"] = JsonSerializer.SerializeToElement(mode),
        };
        return DispatchAsync("memory_world_read_knowledge_file", "knowledge_read/v0", args, ct);
    }

    public Task<EngineEnvelopeNode> List(string? subdir = null, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(subdir))
            args["subdir"] = JsonSerializer.SerializeToElement(subdir);
        return DispatchAsync("memory_world_list_knowledge_files", "knowledge_list/v0", args, ct);
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
        var hint = root.TryGetProperty("hint", out var h) ? h.GetString()
            : root.TryGetProperty("error", out var e) ? e.GetString() : null;
        return new EngineEnvelopeNode(schema, ok, json, hint);
    }
}
