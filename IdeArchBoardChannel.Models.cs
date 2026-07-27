#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

internal static partial class IdeArchBoardChannel
{
    public sealed class BoardDoc
    {
        public string Schema { get; set; } = SchemaVersion;
        public string Title { get; set; } = "architecture";
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Last focused role id — promote/elect without role= uses this.</summary>
        public string? FocusRoleId { get; set; }
        public List<RoleSlot> Roles { get; set; } = [];
        public List<BoardEdge> Edges { get; set; } = [];
    }

    public sealed class RoleSlot
    {
        public string Id { get; set; } = "";
        public string Role { get; set; } = "";
        public string Status { get; set; } = "open"; // open|elected|promoted
        public string? Note { get; set; }
        public string? ElectedCandidateId { get; set; }
        public List<Candidate> Candidates { get; set; } = [];
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class Candidate
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>Canonical CodeAnchor wire <c>[F:;M:;K:]</c> (or L/C). Not a bare path.</summary>
        public string? Anchor { get; set; }
        public string? Path { get; set; }
        public string? Symbol { get; set; }
        public string Status { get; set; } = "candidate"; // candidate|elected|rejected
    }

    public sealed class BoardEdge
    {
        public string Id { get; set; } = "";
        public string FromRoleId { get; set; } = "";
        public string ToRoleId { get; set; } = "";
        public string Kind { get; set; } = "feeds";
    }

    static IReadOnlyDictionary<string, JsonElement> FlattenGoArgs(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return args;
        var flat = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        foreach (var p in ga.EnumerateObject())
        {
            if (!flat.ContainsKey(p.Name))
                flat[p.Name] = p.Value.Clone();
        }

        return flat;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()?.Trim(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static List<string> OptList(IReadOnlyDictionary<string, JsonElement> args, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!args.TryGetValue(key, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in el.EnumerateArray())
                {
                    var s = item.ValueKind == JsonValueKind.String
                        ? item.GetString()?.Trim()
                        : item.GetRawText();
                    if (s is { Length: > 0 })
                        list.Add(s);
                }

                return list;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                var raw = el.GetString() ?? "";
                return raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }

        return [];
    }

    static string NormRole(string raw)
    {
        var s = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return s switch
        {
            "compute" or "compute_unit" or "cockpit_compute_unit" => "ccu",
            "display" or "cds_contract" => "cds",
            "ide_display" or "overlay" or "ids_overlay" or "ide_overlays" => "ids",
            "compose" or "composition" => "compositor",
            "view" or "pixels" or "json_surface" => "surface",
            "instr" => "instrument",
            "bus" or "ingestion" => "transport",
            "data" or "acquisition" => "dal",
            _ => s
        };
    }

    static string ShortId(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        go = GoName,
        tool = ToolName,
        error,
        hint,
        roles = RoleLexicon
    };
}
