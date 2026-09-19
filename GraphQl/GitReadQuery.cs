#nullable enable
using System.Text.Json;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier B git-read nested Query — commit/push stay mutate MCP.</summary>
internal sealed class GitReadQuery
{
    readonly CdpGraphQlCall _call;

    public GitReadQuery(CdpGraphQlCall call) => _call = call;

    public Task<EngineEnvelopeNode> Scene(CancellationToken ct = default) =>
        DispatchAsync("git_git_scene", "git_scene/v0", WorkspaceArgs(), ct);

    public Task<EngineEnvelopeNode> Status(CancellationToken ct = default) =>
        DispatchAsync("git_status", "git_status/v0", WorkspaceArgs(), ct);

    public Task<EngineEnvelopeNode> Diff(string? path = null, bool staged = false, CancellationToken ct = default)
    {
        var args = WorkspaceArgs();
        if (!string.IsNullOrWhiteSpace(path))
            args["path"] = JsonSerializer.SerializeToElement(path);
        args["staged"] = JsonSerializer.SerializeToElement(staged);
        return DispatchAsync("git_diff", "git_diff/v0", args, ct);
    }

    public Task<EngineEnvelopeNode> Preflight(bool staged = false, CancellationToken ct = default)
    {
        var args = WorkspaceArgs();
        args["staged"] = JsonSerializer.SerializeToElement(staged);
        return DispatchAsync("git_preflight", "git_preflight/v0", args, ct);
    }

    Dictionary<string, JsonElement> WorkspaceArgs()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var ws = _call.Session.ScmRoot ?? _call.Session.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(ws))
            args["workspace_path"] = JsonSerializer.SerializeToElement(ws);
        return args;
    }

    async Task<EngineEnvelopeNode> DispatchAsync(
        string tool,
        string schema,
        Dictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        var ws = _call.Session.ScmRoot ?? _call.Session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(ws))
        {
            var payload = new { ok = true, schema, empty = true, hint = "no_project: cdp_open a repo before git query" };
            return new EngineEnvelopeNode(schema, true, JsonSerializer.Serialize(payload), payload.hint);
        }

        if (_call.DispatchToolAsync is null)
            throw new GraphQLException($"dispatch_unavailable: need DispatchToolAsync for {tool}");

        var json = await _call.DispatchToolAsync(tool, args, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ok = !(root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False);
        var sch = root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? schema
            : schema;
        var hint = root.TryGetProperty("hint", out var h) ? h.GetString()
            : root.TryGetProperty("error", out var e) ? e.GetString() : null;
        return new EngineEnvelopeNode(sch!, ok, json, hint);
    }
}
