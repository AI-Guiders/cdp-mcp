#nullable enable
using System.Text.Json;
using Cdp.ScriptableIde;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>Tier A symbol declaration/usages — rename stays mutate MCP.</summary>
internal static class SymbolReadResolvers
{
    public static async Task<SymbolReadResult> SymbolAsync(
        CdpGraphQlCall call,
        string name,
        string? file = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (string.IsNullOrWhiteSpace(call.Session.ProjectRoot)
            && string.IsNullOrWhiteSpace(call.Session.SolutionOrProjectPath))
        {
            return new SymbolReadResult(name, file, null, Array.Empty<GotoHitNode>(),
                "no_project: cdp_open before symbol");
        }

        if (call.DispatchToolAsync is null)
            throw new GraphQLException("dispatch_unavailable: need DispatchToolAsync for symbol");

        var declArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["name"] = JsonSerializer.SerializeToElement(name),
        };
        if (!string.IsNullOrWhiteSpace(file))
            declArgs["path"] = JsonSerializer.SerializeToElement(file);

        // Prefer goto (works across languages) then find_usages when we have an anchor.
        var gotoArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement("t " + name),
            ["first"] = JsonSerializer.SerializeToElement(10),
        };
        var gotoJson = await call.DispatchToolAsync("cdp_goto", gotoArgs, ct).ConfigureAwait(false);
        var declaration = ParseFirstAnchor(gotoJson);

        var usages = new List<GotoHitNode>();
        if (declaration is not null)
        {
            var span = declaration.ToSpan();
            var useArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["path"] = JsonSerializer.SerializeToElement(span.File ?? file ?? ""),
                ["line"] = JsonSerializer.SerializeToElement(span.LineStart is > 0 ? span.LineStart.Value : 1),
            };
            try
            {
                var useJson = await call.DispatchToolAsync("find_usages", useArgs, ct).ConfigureAwait(false);
                usages.AddRange(ParseGotoHits(useJson));
            }
            catch
            {
                // empty≠error: usages optional when LSP unavailable
            }
        }

        return new SymbolReadResult(name, file, declaration, usages, null);
    }

    static Anchor? ParseFirstAnchor(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                var wire = hit.TryGetProperty("anchor", out var a) ? a.GetString() : null;
                if (!string.IsNullOrWhiteSpace(wire))
                    return Anchor.Parse(wire!);
            }
        }
        return null;
    }

    static List<GotoHitNode> ParseGotoHits(string json)
    {
        var list = new List<GotoHitNode>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonElement arr;
        if (root.TryGetProperty("hits", out var h) && h.ValueKind == JsonValueKind.Array)
            arr = h;
        else if (root.TryGetProperty("locations", out var loc) && loc.ValueKind == JsonValueKind.Array)
            arr = loc;
        else
            return list;

        foreach (var hit in arr.EnumerateArray())
        {
            var wire = hit.TryGetProperty("anchor", out var a) ? a.GetString() : null;
            var name = hit.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var kind = hit.TryGetProperty("kind", out var k) ? k.GetString() ?? "usage" : "usage";
            if (string.IsNullOrWhiteSpace(wire))
            {
                var path = hit.TryGetProperty("path", out var p) ? p.GetString()
                    : hit.TryGetProperty("file", out var f) ? f.GetString() : null;
                var line = hit.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : 0;
                if (!string.IsNullOrWhiteSpace(path) && line > 0)
                    wire = Anchor.File(path!).Line(line).ToWire();
            }
            if (string.IsNullOrWhiteSpace(wire))
                continue;
            list.Add(new GotoHitNode(kind, name, 0, Anchor.Parse(wire!)));
        }
        return list;
    }
}

public sealed class SymbolReadResult
{
    public SymbolReadResult(
        string name,
        string? file,
        Anchor? declaration,
        IReadOnlyList<GotoHitNode> usages,
        string? hint)
    {
        Name = name;
        File = file;
        Declaration = declaration;
        Usages = usages;
        Hint = hint;
    }

    public string Name { get; }
    public string? File { get; }
    public Anchor? Declaration { get; }
    public IReadOnlyList<GotoHitNode> Usages { get; }
    public string? Hint { get; }
}
