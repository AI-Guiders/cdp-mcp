#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class CdpPeekChannel
{
    static readonly HashSet<string> OutlineExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".json", ".jsonc", ".yaml", ".yml", ".toml"
    };

    static object PeekOutline(
        SessionContext session,
        string absPath,
        IReadOnlyDictionary<string, JsonElement> args,
        string? bindNote)
    {
        var rel = Rel(session.ProjectRoot, absPath);
        if (!File.Exists(absPath))
        {
            return Fail("not_found", $"File not found: {absPath}",
                "Dig @intent files or fix path= (FULL rel from session root).");
        }

        var ext = Path.GetExtension(absPath);
        var totalLines = CountLines(absPath);
        var kind = ext.ToLowerInvariant() switch
        {
            ".md" or ".markdown" => "markdown",
            ".json" or ".jsonc" => "json",
            ".yaml" or ".yml" => "yaml",
            ".toml" => "toml",
            _ => null
        };

        if (kind is null)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                tool = ToolName,
                mode = "outline",
                path = absPath,
                rel,
                bind_note = bindNote,
                ext,
                total_lines = totalLines,
                supported = false,
                hint = "No cheap structural markers for this type — read by slice (offset/limit) or cdp_get_document_symbols for code."
            };
        }

        var entries = kind switch
        {
            "markdown" => MarkdownOutline(rel, absPath),
            "json" => JsonOutline(absPath),
            _ => null
        };

        if (entries is null || entries.Count == 0)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                tool = ToolName,
                mode = "outline",
                path = absPath,
                rel,
                bind_note = bindNote,
                ext,
                kind,
                total_lines = totalLines,
                count = 0,
                hint = "No structural markers found — read by slice (offset/limit)."
            };
        }

        return new
        {
            schema = SchemaVersion,
            ok = true,
            tool = ToolName,
            mode = "outline",
            path = absPath,
            rel,
            bind_note = bindNote,
            ext,
            kind,
            total_lines = totalLines,
            count = entries.Count,
            entries,
            hint = $"Jump: cdp_peek path={rel} anchor=[F:;L:<line>] or offset=<line>."
        };
    }

    static List<object> MarkdownOutline(string rel, string absPath)
    {
        var list = new List<object>();
        var n = 0;
        foreach (var raw in File.ReadLines(absPath))
        {
            n++;
            if (raw.Length == 0 || raw[0] != '#') continue;
            var level = 0;
            while (level < raw.Length && level < 6 && raw[level] == '#') level++;
            if (level == 0 || level == raw.Length) continue;
            var c = raw[level];
            if (c != ' ' && c != '\t') continue;
            var name = raw[level..].Trim();
            if (name.Length == 0) continue;
            list.Add(new
            {
                level,
                name,
                line = n,
                anchor = BracketLocate.Format(new BracketLocate.Span(rel, null, n, null))
            });
        }
        return list;
    }

    static List<object>? JsonOutline(string absPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(absPath));
            var list = new List<object>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    list.Add(new { name = prop.Name, kind = JsonKindName(prop.Value.ValueKind), line = 0 });
            }
            else
            {
                list.Add(new { name = "$root", kind = JsonKindName(doc.RootElement.ValueKind), line = 0 });
            }
            return list;
        }
        catch
        {
            return null;
        }
    }

    static string JsonKindName(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Null => "null",
        _ => "value"
    };

    static string? OutlineHint(string rel, string absPath, int totalLines)
    {
        if (totalLines < 200) return null;
        var ext = Path.GetExtension(absPath);
        if (!OutlineExts.Contains(ext)) return null;
        return $"Large {ext.TrimStart('.').ToLowerInvariant()}: outline first — cdp_peek path={rel} mode=outline";
    }
}
