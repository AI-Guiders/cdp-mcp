using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class EditorComfort
{
    static object[] ComfortNext(DocBuffer buf) =>
    [
        new { go = "undo", label = "Undo", why = "edit stack" },
        new { go = "clipboard", label = "Clipboard", why = AnyClipboard() ? "session clip holds text" : "empty — copy/cut" },
        new { go = "cut", label = "Cut", why = "anchor= → clip + remove" },
        new { go = "history", label = "History", why = $"doc {buf.DocId}" },
        new { go = "back", label = "Nav back", why = "locus stack" }
    ];

    static object ClipSummary() => SessionClipboard.Summary();

    static object NavPulse()
    {
        lock (Gate)
        {
            return new
            {
                current = NavCurrent,
                back = NavBack.Count,
                forward = NavForward.Count
            };
        }
    }

    static string NormalizeWireOrFile(SessionContext session, string raw)
    {
        var t = raw.Trim();
        if (t.Contains('[') || t.Contains("F:", StringComparison.OrdinalIgnoreCase))
            return NormalizeWire(t);
        try
        {
            return WireFile(session, ResolveUserPath(session, t));
        }
        catch
        {
            return NormalizeWire(t);
        }
    }

    static string NormalizeWire(string wire)
    {
        var t = wire.Trim();
        if (!t.StartsWith('['))
            t = "[" + t;
        if (!t.EndsWith(']'))
            t += "]";
        return t;
    }

    static string WireFile(SessionContext session, string absolutePath) =>
        BracketLocate.Format(new BracketLocate.Span(FileLabel(session, absolutePath), null, null, null));

    static string WireLines(SessionContext session, string absolutePath, int start, int end) =>
        BracketLocate.Format(new BracketLocate.Span(
            FileLabel(session, absolutePath),
            null,
            start,
            end == start ? null : end));

    static string FileLabel(SessionContext session, string absolutePath)
    {
        var root = session.ProjectRoot;
        if (root is { Length: > 0 })
        {
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(absolutePath);
            if (full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return full[(fullRoot.Length + 1)..].Replace('\\', '/');
            }
        }

        return Path.GetFileName(absolutePath);
    }

    static string ResolveUserPath(SessionContext session, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Trim();
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);
        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static int CountLines(string text)
    {
        if (text.Length == 0)
            return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                n++;
        }

        return n;
    }

    static int LastLineLength(string text)
    {
        var i = text.LastIndexOf('\n');
        return i < 0 ? text.Length : text.Length - i - 1;
    }

    static int LineLengthAt(string text, int line1Based)
    {
        var start = OffsetOf(text, line1Based, 1);
        var end = start;
        while (end < text.Length && text[end] != '\n')
            end++;
        if (end > start && text[end - 1] == '\r')
            end--;
        return end - start;
    }

    static (int Line, int Col) LineColAt(string text, int index)
    {
        var line = 1;
        var col = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
                col++;
        }

        return (line, col);
    }

    static int OffsetOf(string text, int line, int column)
    {
        var lineIdx = 1;
        var i = 0;
        while (i < text.Length && lineIdx < line)
        {
            if (text[i] == '\n')
                lineIdx++;
            i++;
        }

        if (lineIdx != line)
            throw new ArgumentException($"Line {line} is past end of buffer ({lineIdx} lines).");
        var col = 1;
        while (i < text.Length && col < column)
        {
            if (text[i] == '\n')
                break;
            i++;
            col++;
        }

        if (col != column)
            throw new ArgumentException($"Column {column} is past end of line {line}.");
        return i;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int? IntOrNull(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number)
            return null;
        return el.TryGetInt32(out var n) ? n : null;
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
            JsonValueKind.String when el.GetString() is "1" or "yes" or "on" => true,
            JsonValueKind.String when el.GetString() is "0" or "no" or "off" => false,
            _ => defaultValue
        };
    }
}
