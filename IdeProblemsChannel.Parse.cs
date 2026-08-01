#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Diag parse + arg helpers for go=problems.</summary>
internal static partial class IdeProblemsChannel
{
    sealed record ParsedItem(
        string Severity,
        string Message,
        string? Code,
        int Line,
        int EndLine,
        string? Anchor);

    static bool TryParseItems(
        string json,
        string bufferPath,
        string? projectRoot,
        out List<ParsedItem> items)
    {
        items = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var arr in FindItemArrays(root))
            {
                foreach (var el in arr.EnumerateArray())
                    if (TryMapItem(el, bufferPath, projectRoot, out var item))
                        items.Add(item);
            }

            return items.Count > 0 || root.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    static IEnumerable<JsonElement> FindItemArrays(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var name in new[] { "items", "diagnostics", "Diagnostics" })
        {
            if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                yield return arr;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "diagnostics" })
            {
                if (data.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    yield return arr;
            }
        }

        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "diagnostics" })
            {
                if (result.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    yield return arr;
            }
        }
    }

    static bool TryMapItem(JsonElement el, string bufferPath, string? projectRoot, out ParsedItem item)
    {
        item = default!;
        if (el.ValueKind != JsonValueKind.Object)
            return false;

        var message = PropString(el, "message") ?? PropString(el, "Message") ?? "";
        if (message.Length == 0)
            return false;

        var severity = PropString(el, "severity") ?? PropString(el, "Severity") ?? "info";
        var code = PropString(el, "id") ?? PropString(el, "code") ?? PropString(el, "Code");
        var anchor = PropString(el, "anchor") ?? PropString(el, "Anchor");

        var line = PropInt(el, "line")
            ?? PropInt(el, "Line")
            ?? PropInt(el, "start_line")
            ?? 0;
        var endLine = PropInt(el, "end_line")
            ?? PropInt(el, "EndLine")
            ?? line;

        if (el.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object)
        {
            line = PropInt(range, "start_line") ?? PropInt(range, "StartLine") ?? line;
            endLine = PropInt(range, "end_line") ?? PropInt(range, "EndLine") ?? endLine;
        }

        if (line <= 0 && anchor is { Length: > 0 })
        {
            try
            {
                var span = BracketLocate.Parse(anchor);
                line = span.LineStart ?? 0;
                endLine = span.LineEnd ?? line;
            }
            catch
            {
                // keep 0
            }
        }

        if (line <= 0)
            line = 1;
        if (endLine < line)
            endLine = line;

        if (anchor is null or { Length: 0 })
            anchor = FormatAnchor(projectRoot, bufferPath, line, endLine);

        item = new ParsedItem(severity, message, code, line, endLine, anchor);
        return true;
    }

    static string FormatAnchor(string? root, string absolutePath, int line, int endLine)
    {
        var label = Rel(root, absolutePath).Replace('\\', '/');
        return BracketLocate.Format(new BracketLocate.Span(
            label,
            null,
            line,
            endLine == line ? null : endLine));
    }

    static string Rel(string? root, string abs)
    {
        if (root is not { Length: > 0 })
            return abs.Replace('\\', '/');
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var a = Path.GetFullPath(abs);
            if (a.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            {
                var rel = a[r.Length..].TrimStart('\\', '/');
                return rel.Replace('\\', '/');
            }
        }
        catch
        {
            // fall through
        }

        return Path.GetFileName(abs);
    }

    static string NormalizeSeverity(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "0" or "error" or "err" or "fatal" or "critical" => "error",
            "1" or "warning" or "warn" => "warning",
            "2" or "info" or "information" or "hint" or "note" => "info",
            _ when s.Contains("error", StringComparison.Ordinal) => "error",
            _ when s.Contains("warn", StringComparison.Ordinal) => "warning",
            _ => "info"
        };
    }

    static string Glyph(string severity) => severity switch
    {
        "error" => "!",
        "warning" => "*",
        _ => "·"
    };

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null)
            return merged;

        foreach (var kv in args)
        {
            if (kv.Key is "go_args" && kv.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in kv.Value.EnumerateObject())
                    merged[p.Name] = p.Value.Clone();
                continue;
            }

            merged[kv.Key] = kv.Value.Clone();
        }

        return merged;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            _ => null
        };
    }

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => fallback
        };
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => defaultValue
        };
    }

    static string? PropString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    static int? PropInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
            return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n))
            return n;
        return null;
    }
}
