#nullable enable
using System.Text.Json;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier B situational pulses — execute verbs stay MCP.</summary>
internal static class SituationalReadResolvers
{
    public static Task<EngineEnvelopeNode> HealthAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_health", "health/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> RecentAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_recent", "recent/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> LifecycleAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_lifecycle_scene", "lifecycle_scene/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> ProjectSceneAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_project_scene", "project_scene/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> EditorSceneAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_editor_scene", "editor_scene/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> ShellSceneAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_shell_scene", "shell_scene/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> CanonStackAsync(CdpGraphQlCall call, CancellationToken ct = default) =>
        DispatchAsync(call, "cdp_canon_stack", "canon_stack/v0", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public static Task<EngineEnvelopeNode> LastBuildAsync(CdpGraphQlCall call, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["job"] = JsonSerializer.SerializeToElement("build"),
        };
        return DispatchAsync(call, "cdp_lifecycle_last", "lifecycle_last/v0", args, ct);
    }

    public static Task<EngineEnvelopeNode> LastTestAsync(CdpGraphQlCall call, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["job"] = JsonSerializer.SerializeToElement("test"),
        };
        return DispatchAsync(call, "cdp_lifecycle_last", "lifecycle_last/v0", args, ct);
    }

    static async Task<EngineEnvelopeNode> DispatchAsync(
        CdpGraphQlCall call,
        string tool,
        string schema,
        Dictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (call.DispatchToolAsync is null)
            throw new GraphQLException($"dispatch_unavailable: need DispatchToolAsync for {tool}");

        var json = await call.DispatchToolAsync(tool, args, ct).ConfigureAwait(false);
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
