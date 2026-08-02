#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Backends;

namespace CdpMcp;
internal static partial class SemanticMap
{
    sealed record Hit(string abs, string rel, string anchor, string why, int? line);
    static List<Hit> BuildHits(string roslynJson, SessionContext session, int max)
    {
        var list = new List<Hit>();
        try
        {
            using var doc = JsonDocument.Parse(roslynJson);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.TryGetProperty("items", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : root.TryGetProperty("nodes", out var n) && n.ValueKind == JsonValueKind.Array ? n.EnumerateArray() : [];
            foreach (var item in items)
            {
                if (list.Count >= max)
                    break;
                var path = item.TryGetProperty("path", out var p) ? p.GetString() : item.TryGetProperty("file_path", out var fp) ? fp.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var why = item.TryGetProperty("kind", out var k) ? k.GetString() : item.TryGetProperty("relation_kind", out var rk) ? rk.GetString() : item.TryGetProperty("label", out var lb) ? lb.GetString() : "related";
                int? line = item.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var li) ? li : null;
                string abs;
                try
                {
                    abs = Path.GetFullPath(path);
                }
                catch
                {
                    abs = path;
                }

                var rel = Rel(session, abs);
                var wire = line is int l ? $"[F:{rel.Replace('\\', '/')}; L:{l}]" : $"[F:{rel.Replace('\\', '/')}]";
                list.Add(new Hit(abs, rel.Replace('\\', '/'), wire, why ?? "related", line));
            }
        }
        catch (JsonException)
        {
        }

        return list;
    }

    static string Rel(SessionContext session, string abs)
    {
        var root = session.ScmRoot ?? session.ProjectRoot;
        if (root is null)
            return abs;
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var a = Path.GetFullPath(abs);
            if (a.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return a[r.Length..].TrimStart('\\', '/');
        }
        catch
        {
        }

        return abs;
    }

    static string ResolvePath(SessionContext session, string pathArg)
    {
        if (Path.IsPathRooted(pathArg))
            return Path.GetFullPath(pathArg);
        var root = session.ScmRoot ?? session.ProjectRoot ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, pathArg));
    }

    static bool TryFileFromWire(string wire, SessionContext session, out string abs)
    {
        abs = "";
        var raw = wire.Trim().Trim('[', ']');
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.StartsWith("F:", StringComparison.OrdinalIgnoreCase))
                continue;
            abs = ResolvePath(session, part[2..].Trim());
            return true;
        }

        return false;
    }

    static void TryLineFromWire(string wire, ref int? line)
    {
        if (line is not null)
            return;
        var raw = wire.Trim().Trim('[', ']');
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("L:", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[2..].Trim(), out var ln))
            {
                line = ln;
                return;
            }
        }
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) => args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var el))
            return fallback;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s))
            return s;
        return fallback;
    }

    static int? IntOrNull(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s))
            return s;
        return null;
    }
}