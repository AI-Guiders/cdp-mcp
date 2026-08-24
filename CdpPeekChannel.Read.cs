#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class CdpPeekChannel
{
    public const int DefaultLimit = 120;
    public const int HardLimit = 500;
    public const int DefaultPad = 20;
    public const int MaxBatch = 8;
    public const int MaxChars = 48_000;
    public const long MaxFileBytes = 5_000_000;

    sealed record LineEntry(int N, string Text, string? Anchor);

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".svg"
    };

    static object PeekBatch(
        SessionContext session,
        LanguageRegistry langs,
        IReadOnlyDictionary<string, JsonElement> args,
        List<string> paths)
    {
        if (paths.Count > MaxBatch)
        {
            return Fail("batch_too_large", $"paths[] count={paths.Count} max={MaxBatch}",
                "Split batch or peek files in smaller groups.");
        }

        var bind = BoolOr(args, "bind", defaultValue: true);
        var perLimit = ClampLimit(args, DefaultLimit / 2);
        var budget = MaxChars;
        var files = new List<object>();
        var truncated = false;

        foreach (var p in paths)
        {
            if (budget <= 0)
            {
                truncated = true;
                break;
            }

            var abs = ResolvePath(session, langs, p, Opt(args, "scope"), bind, out _, out var resolveErr);
            if (abs is null)
            {
                files.Add(new { ok = false, path = p, error = resolveErr ?? "path_invalid" });
                continue;
            }

            var sliceArgs = SliceArgsForBatch(args, perLimit, budget);
            var peek = PeekFile(session, abs, sliceArgs, bindNote: null);
            files.Add(peek);

            if (TryReadChars(peek, out var used))
                budget -= used;
        }

        return new
        {
            schema = SchemaVersion,
            ok = true,
            tool = ToolName,
            mode = "batch",
            count = files.Count,
            truncated,
            files,
            hint = truncated
                ? "Char budget exhausted — lower limit= or peek remaining paths separately."
                : "Mutate chain: lines[].anchor → cdp_edit_sniper aim="
        };
    }

    static bool TryReadChars(object peek, out int chars)
    {
        chars = 0;
        try
        {
            var json = JsonSerializer.Serialize(peek);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("chars", out var c) && c.TryGetInt32(out chars))
                return true;
            if (doc.RootElement.TryGetProperty("text", out var t) && t.GetString() is { } s)
            {
                chars = s.Length;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    static Dictionary<string, JsonElement> SliceArgsForBatch(
        IReadOnlyDictionary<string, JsonElement> args,
        int limit,
        int budgetChars)
    {
        var cap = Math.Min(limit, Math.Max(20, budgetChars / 400));
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "paths" or "path")
                continue;
            dict[kv.Key] = kv.Value;
        }

        dict["limit"] = JsonSerializer.SerializeToElement(cap);
        return dict;
    }

    static object PeekFile(
        SessionContext session,
        string absPath,
        IReadOnlyDictionary<string, JsonElement> args,
        string? bindNote)
    {
        var rel = Rel(session.ProjectRoot, absPath);
        if (!File.Exists(absPath))
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                tool = ToolName,
                mode = "file",
                path = absPath,
                rel,
                error = "not_found",
                hint = "Dig @intent files or fix path= (FULL rel from session root)."
            };
        }

        var ext = Path.GetExtension(absPath);
        if (ImageExtensions.Contains(ext))
        {
            var info = new FileInfo(absPath);
            return new
            {
                schema = SchemaVersion,
                ok = true,
                tool = ToolName,
                mode = "image",
                path = absPath,
                rel,
                bytes = info.Length,
                extension = ext,
                hint = "Text not inlined — cdp_see path= for vision (PNG/JPEG/WebP)."
            };
        }

        var len = new FileInfo(absPath).Length;
        if (len > MaxFileBytes)
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                tool = ToolName,
                mode = "file",
                path = absPath,
                rel,
                error = "file_too_large",
                bytes = len,
                max_bytes = MaxFileBytes,
                hint = "Narrow with anchor= land or shell head/tail."
            };
        }

        if (LooksBinary(absPath))
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                tool = ToolName,
                mode = "file",
                path = absPath,
                rel,
                error = "binary",
                hint = "Not a text file — cdp_see for images or shell for opaque blobs."
            };
        }

        var totalLines = CountLines(absPath);
        var wire = Opt(args, "anchor") ?? Opt(args, "at");
        int startLine;
        int limit;
        if (wire is { Length: > 0 } && TryAnchorWindow(wire, args, out var anchorLine, out var pad))
        {
            startLine = Math.Max(1, anchorLine - pad);
            limit = Math.Min(HardLimit, pad * 2 + 1);
        }
        else
        {
            startLine = ResolveStartLine(args, totalLines);
            limit = ClampLimit(args, DefaultLimit);
            if (startLine + limit - 1 > totalLines)
                limit = Math.Max(0, totalLines - startLine + 1);
        }

        var includeAnchors = BoolOr(args, "include_anchors", defaultValue: true);
        var wantText = !BoolOr(args, "structured_only", defaultValue: false);
        var wantLines = !BoolOr(args, "text_only", defaultValue: false);
        if (!wantText && !wantLines)
        {
            wantText = true;
            wantLines = true;
        }

        var slice = ReadSlice(absPath, startLine, limit, rel, includeAnchors);
        var textBlock = wantText ? FormatNumberedText(slice) : null;
        var chars = textBlock?.Length ?? slice.Sum(l => l.Text.Length + 12);

        var returned = slice.Count;
        var hasMore = startLine + returned - 1 < totalLines;
        var nextOffset = hasMore ? startLine + returned : (int?)null;

        return new
        {
            schema = SchemaVersion,
            ok = true,
            tool = ToolName,
            mode = "file",
            path = absPath,
            rel,
            bind_note = bindNote,
            total_lines = totalLines,
            offset = startLine,
            limit,
            returned,
            has_more = hasMore,
            next_offset = nextOffset,
            chars,
            outline_hint = OutlineHint(rel, absPath, totalLines),
            text = textBlock,
            lines = wantLines
                ? slice.Select(l => new { n = l.N, text = l.Text, anchor = l.Anchor }).ToList()
                : null,
            hint = hasMore
                ? $"More: cdp_peek path={rel} offset={nextOffset}"
                : "Mutate: cdp_buffer op=edit anchor= from lines[].anchor"
        };
    }

    static List<LineEntry> ReadSlice(
        string absPath,
        int startLine,
        int limit,
        string rel,
        bool includeAnchors)
    {
        var list = new List<LineEntry>(Math.Min(limit, 64));
        var lineNo = 0;
        foreach (var line in File.ReadLines(absPath))
        {
            lineNo++;
            if (lineNo < startLine)
                continue;
            if (list.Count >= limit)
                break;

            list.Add(new LineEntry(
                lineNo,
                line,
                includeAnchors
                    ? BracketLocate.Format(new BracketLocate.Span(rel, null, lineNo, null))
                    : null));
        }

        return list;
    }

    static string FormatNumberedText(IReadOnlyList<LineEntry> lines)
    {
        var sb = new StringBuilder();
        foreach (var item in lines)
            sb.Append(item.N.ToString().PadLeft(6)).Append('|').AppendLine(item.Text);

        return sb.ToString().TrimEnd('\r', '\n');
    }

    static int CountLines(string path)
    {
        var count = 0;
        foreach (var _ in File.ReadLines(path))
            count++;
        return count == 0 ? 1 : count;
    }

    static int ResolveStartLine(IReadOnlyDictionary<string, JsonElement> args, int total)
    {
        var offset = IntOr(args, "offset") ?? IntOr(args, "start_line");
        if (offset is null or 0)
            return 1;

        if (offset > 0)
            return Math.Min(offset.Value, Math.Max(1, total));

        var fromEnd = -offset.Value;
        return Math.Max(1, total - fromEnd + 1);
    }

    static int ClampLimit(IReadOnlyDictionary<string, JsonElement> args, int defaultLimit)
    {
        var raw = IntOr(args, "limit") ?? IntOr(args, "lines") ?? defaultLimit;
        return Math.Clamp(raw, 1, HardLimit);
    }

    static bool TryAnchorWindow(
        string wire,
        IReadOnlyDictionary<string, JsonElement> args,
        out int line,
        out int pad)
    {
        line = 0;
        pad = IntOr(args, "pad") ?? DefaultPad;
        pad = Math.Clamp(pad, 0, 80);
        try
        {
            var span = BracketLocate.Parse(wire);
            if (span.LineStart is int ls && ls > 0)
            {
                line = ls;
                return true;
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    static bool LooksBinary(string path)
    {
        try
        {
            Span<byte> buf = stackalloc byte[8192];
            using var fs = File.OpenRead(path);
            var read = fs.Read(buf);
            for (var i = 0; i < read; i++)
            {
                if (buf[i] == 0)
                    return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
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
                return a[r.Length..].TrimStart('\\', '/').Replace('\\', '/');
        }
        catch
        {
            // fall through
        }

        return Path.GetFileName(abs);
    }
}
