#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// JSON anchor family (J:) — resolves a $-path (MemberKey) to a token span via Utf8JsonReader
/// with line/col tracking. .jsonc-friendly: comments and trailing commas are skipped.
/// Spec: "$.a.b[2].c" (leading $ optional; array index → "#n" segment). Resolves to the VALUE
/// span; objects/arrays cover the whole {…}/[…] block; a property resolves to its value.
/// Duplicate keys: first occurrence wins.
/// </summary>
internal static class JsonAnchorResolve
{
    const char SegSep = '\u0001';

    public static bool TryResolve(
        string path,
        string text,
        BracketLocate.Span span,
        out BracketSyntaxResolve.TextRange range,
        out string detail)
    {
        range = new BracketSyntaxResolve.TextRange(1, 1, 1, 1);
        detail = "";

        var spec = span.MemberKey?.Trim();
        if (string.IsNullOrWhiteSpace(spec))
        {
            // No J axis: whole-file locus.
            var allLines = text.Replace("\r\n", "\n").Split('\n');
            var lastLen = allLines.Length > 0 ? allLines[^1].Length : 0;
            range = new BracketSyntaxResolve.TextRange(1, 1, allLines.Length, Math.Max(1, lastLen + 1));
            detail = "json:file";
            return true;
        }

        var segments = ParseSpec(spec);
        if (segments is null)
        {
            detail = "json_spec_empty";
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var lineStarts = ComputeLineStarts(bytes);
        var entries = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);

        if (!Collect(bytes, entries, out var parseError))
        {
            detail = parseError ?? "json_parse";
            return false;
        }

        var key = string.Join(SegSep, segments);
        if (!entries.TryGetValue(key, out var hit))
        {
            detail = "json_path_not_found";
            return false;
        }

        var (ls, cs) = OffsetToPos(lineStarts, hit.Start);
        var (le, ce) = OffsetToPos(lineStarts, hit.End); // exclusive end → col after last char
        range = new BracketSyntaxResolve.TextRange(ls, cs, le, ce);
        detail = "json:path";
        return true;
    }

    /// <summary>"$.a.b[2]" / "a.b" → segments; array index → "#n". Null when spec reduces to empty.</summary>
    internal static List<string>? ParseSpec(string spec)
    {
        var s = spec.Trim();
        if (s.StartsWith("$", StringComparison.Ordinal))
            s = s[1..];
        s = s.TrimStart('.');

        var segments = new List<string>();
        foreach (var raw in s.Split('.'))
        {
            var name = raw;
            while (true)
            {
                var open = name.IndexOf('[');
                if (open < 0)
                    break;
                var close = name.IndexOf(']', open);
                if (close < open)
                    return null;
                var head = name[..open].Trim();
                var idx = name[(open + 1)..close].Trim();
                if (head.Length > 0)
                    segments.Add(head);
                if (!int.TryParse(idx, out var i) || i < 0)
                    return null;
                segments.Add("#" + i);
                name = name[(close + 1)..];
            }

            if (name.Length > 0)
                segments.Add(name);
        }

        return segments;
    }

    static bool Collect(byte[] bytes, Dictionary<string, (int Start, int End)> entries, out string? error)
    {
        error = null;
        var reader = new Utf8JsonReader(
            bytes,
            new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var path = new List<string>();
        var pendingProp = (string?)null;
        var frames = new Stack<Frame>();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    pendingProp = reader.GetString();
                    break;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                {
                    AppendOwnSegment(path, pendingProp, frames);
                    pendingProp = null;
                    frames.Push(new Frame
                    {
                        PathLen = path.Count,
                        Start = (int)reader.TokenStartIndex,
                        IsArray = reader.TokenType == JsonTokenType.StartArray,
                        Counter = 0,
                    });
                    break;
                }

                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                {
                    var frame = frames.Pop();
                    var end = (int)reader.TokenStartIndex + 1;
                    entries[string.Join(SegSep, path)] = (frame.Start, end);
                    while (path.Count > frame.PathLen)
                        path.RemoveAt(path.Count - 1);
                    if (frames.Count > 0 && frames.Peek().IsArray)
                        frames.Peek().Counter++;
                    break;
                }

                default:
                {
                    var start = (int)reader.TokenStartIndex;
                    var end = PrimitiveEnd(reader, start);
                    var segs = new List<string>(path);
                    if (pendingProp is not null)
                        segs.Add(pendingProp);
                    else if (frames.Count > 0 && frames.Peek().IsArray)
                        segs.Add("#" + frames.Peek().Counter);
                    entries[string.Join(SegSep, segs)] = (start, end);

                    pendingProp = null;
                    if (frames.Count > 0 && frames.Peek().IsArray)
                        frames.Peek().Counter++;
                    break;
                }
            }
        }

        entries[string.Empty] = (0, bytes.Length); // "$" → whole document
        return true;
    }

    static void AppendOwnSegment(List<string> path, string? pendingProp, Stack<Frame> frames)
    {
        if (pendingProp is not null)
        {
            path.Add(pendingProp);
            return;
        }

        if (frames.Count > 0 && frames.Peek().IsArray)
            path.Add("#" + frames.Peek().Counter);
    }

    static int PrimitiveEnd(Utf8JsonReader reader, int start) =>
        reader.TokenType switch
        {
            JsonTokenType.String or JsonTokenType.Number => start + reader.ValueSpan.Length
                + (reader.TokenType == JsonTokenType.String ? 2 : 0),
            JsonTokenType.True => start + 4,
            JsonTokenType.False => start + 5,
            JsonTokenType.Null => start + 4,
            _ => start + 1,
        };

    static int[] ComputeLineStarts(byte[] bytes)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == 0x0A)
                starts.Add(i + 1);
        return starts.ToArray();
    }

    static (int Line, int Col) OffsetToPos(int[] lineStarts, int offset)
    {
        var lo = 0;
        var hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset)
                lo = mid;
            else
                hi = mid - 1;
        }

        return (lo + 1, offset - lineStarts[lo] + 1);
    }

    sealed class Frame
    {
        public int PathLen;
        public int Start;
        public bool IsArray;
        public int Counter;
    }
}