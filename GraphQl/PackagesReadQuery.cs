#nullable enable
using System.Text.Json;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier C pkg-read nested Query — mutate stays cdp_pkg_add/remove/update/fix_vuln.</summary>
internal sealed class PackagesReadQuery
{
    readonly CdpGraphQlCall _call;

    public PackagesReadQuery(CdpGraphQlCall call) => _call = call;

    public Task<EngineEnvelopeNode> List(CancellationToken ct = default) =>
        DispatchAsync("cdp_pkg_list", "pkg_list", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public Task<EngineEnvelopeNode> Find(string query, int take = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (take < 1) take = 1;
        if (take > 50) take = 50;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement(query),
            ["take"] = JsonSerializer.SerializeToElement(take),
        };
        return DispatchAsync("cdp_pkg_find", "pkg_find", args, ct);
    }

    public Task<EngineEnvelopeNode> Outdated(CancellationToken ct = default) =>
        DispatchAsync("cdp_pkg_outdated", "pkg_outdated", new Dictionary<string, JsonElement>(StringComparer.Ordinal), ct);

    public Task<EngineEnvelopeNode> Audit(bool includeTransitive = true, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["include_transitive"] = JsonSerializer.SerializeToElement(includeTransitive),
        };
        return DispatchAsync("cdp_pkg_audit", "pkg_audit", args, ct);
    }

    public Task<EngineEnvelopeNode> Latest(string id, bool includePrerelease = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["include_prerelease"] = JsonSerializer.SerializeToElement(includePrerelease),
        };
        return DispatchAsync("cdp_pkg_latest", "pkg_latest", args, ct);
    }

    public Task<EngineEnvelopeNode> SupplyChain(string? root = null, CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(root))
            args["root"] = JsonSerializer.SerializeToElement(root);
        return DispatchAsync("cdp_pkg_supply_chain", "pkg_supply_chain", args, ct);
    }

    public Task<EngineEnvelopeNode> UpgradePlan(
        bool includeTransitive = true,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["include_transitive"] = JsonSerializer.SerializeToElement(includeTransitive),
            ["include_prerelease"] = JsonSerializer.SerializeToElement(includePrerelease),
        };
        return DispatchAsync("cdp_pkg_upgrade_plan", "pkg_upgrade_plan", args, ct);
    }

    async Task<EngineEnvelopeNode> DispatchAsync(
        string tool,
        string kind,
        Dictionary<string, JsonElement> args,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_call.Session.ProjectRoot)
            && string.IsNullOrWhiteSpace(_call.Session.SolutionOrProjectPath))
        {
            var payload = new { ok = true, kind, empty = true, hint = "no_project: cdp_open before packages" };
            return new EngineEnvelopeNode(kind, true, JsonSerializer.Serialize(payload), payload.hint);
        }

        if (_call.DispatchToolAsync is null)
            throw new GraphQLException($"dispatch_unavailable: MetaDispatch must supply DispatchToolAsync for {tool}");

        var json = await _call.DispatchToolAsync(tool, args, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ok = !(root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False);
        var schema = root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString() ?? kind
            : (root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? kind
                : kind);
        var hint = root.TryGetProperty("error", out var e) ? e.GetString()
            : root.TryGetProperty("hint", out var h) ? h.GetString() : null;
        return new EngineEnvelopeNode(schema!, ok, json, hint);
    }
}
