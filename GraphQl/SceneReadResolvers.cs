#nullable enable
using System.Text.Json;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier A leftover + Tier C scene reads → DispatchToolAsync (ADR-0233).</summary>
internal static class SceneReadResolvers
{
    public static async Task<EngineEnvelopeNode> SemanticMapAsync(
        CdpGraphQlCall call,
        string? path = null,
        string? anchor = null,
        string? mode = null,
        int? maxRelated = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(call.Session.ProjectRoot)
            && string.IsNullOrWhiteSpace(call.Session.SolutionOrProjectPath))
        {
            return Empty("semantic_map/v0", "no_project: cdp_open before semanticMap");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["feature"] = JsonSerializer.SerializeToElement("semantic_map"),
            ["mode"] = JsonSerializer.SerializeToElement(
                string.IsNullOrWhiteSpace(mode) ? "related" : mode.Trim()),
        };
        if (!string.IsNullOrWhiteSpace(path))
            args["path"] = JsonSerializer.SerializeToElement(path);
        if (!string.IsNullOrWhiteSpace(anchor))
            args["anchor"] = JsonSerializer.SerializeToElement(anchor);
        if (maxRelated is int m)
            args["max_related"] = JsonSerializer.SerializeToElement(m);

        return await DispatchEnvelopeAsync(call, "cdp_analysis_scene", "semantic_map/v0", args, ct)
            .ConfigureAwait(false);
    }

    public static async Task<EngineEnvelopeNode> CodeClonesAsync(
        CdpGraphQlCall call,
        string? path = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(call.Session.ProjectRoot)
            && string.IsNullOrWhiteSpace(call.Session.SolutionOrProjectPath))
        {
            return Empty("code_clones/v0", "no_project: cdp_open before codeClones");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["feature"] = JsonSerializer.SerializeToElement("clones"),
        };
        if (!string.IsNullOrWhiteSpace(path))
            args["path"] = JsonSerializer.SerializeToElement(path);

        return await DispatchEnvelopeAsync(call, "cdp_analysis_scene", "code_clones/v0", args, ct)
            .ConfigureAwait(false);
    }

    public static async Task<EngineEnvelopeNode> TestSceneAsync(
        CdpGraphQlCall call,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(call.Session.SolutionOrProjectPath)
            && string.IsNullOrWhiteSpace(call.Session.ProjectRoot))
        {
            return Empty("test_scene/v0", "no_project: cdp_open before testScene");
        }

        return await DispatchEnvelopeAsync(
                call,
                "cdp_test_scene",
                "test_scene/v0",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                ct)
            .ConfigureAwait(false);
    }

    static async Task<EngineEnvelopeNode> DispatchEnvelopeAsync(
        CdpGraphQlCall call,
        string tool,
        string defaultSchema,
        Dictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (call.DispatchToolAsync is null)
            throw new GraphQLException($"dispatch_unavailable: MetaDispatch must supply DispatchToolAsync for {tool}");

        var json = await call.DispatchToolAsync(tool, args, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ok = !(root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False);
        var schema = root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? defaultSchema
            : defaultSchema;
        var hint = root.TryGetProperty("hint", out var h) ? h.GetString() : null;
        // empty≠error: recoverable engine ok=false stays envelope, not GraphQLException
        return new EngineEnvelopeNode(schema, ok, json, hint);
    }

    static EngineEnvelopeNode Empty(string schema, string hint)
    {
        var payload = new { ok = true, schema, empty = true, hint };
        return new EngineEnvelopeNode(schema, true, JsonSerializer.Serialize(payload), hint);
    }
}
