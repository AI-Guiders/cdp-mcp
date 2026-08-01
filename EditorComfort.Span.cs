using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class EditorComfort
{
    static void ApplyRestore(DocumentBufferStore store, DocBuffer buf, string text, bool flush)
    {
        store.ApplySetText(buf, text);
        if (flush)
            store.Flush(buf, allowShrink: true);
    }

    static EditStack StackFor(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Stacks.TryGetValue(full, out var stack))
        {
            stack = new EditStack();
            Stacks[full] = stack;
        }

        return stack;
    }

    static DocBuffer ResolveBuf(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        var docId = OptString(args, "doc_id");
        if (path is { Length: > 0 })
            path = ResolveUserPath(session, path);
        else if (docId is null)
        {
            var first = store.All.FirstOrDefault();
            if (first is null)
                throw new ArgumentException("path= / doc_id= or an open buffer required.");
            return first;
        }

        return store.Resolve(path, docId);
    }

    static (string Text, string From) ExtractSpan(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        // Clipboard-only fill: no open buffer required.
        if (OptString(args, "text") is { Length: > 0 } literal
            && OptString(args, "anchor") is null
            && OptString(args, "at") is null
            && OptString(args, "from") is null
            && IntOrNull(args, "start_line") is null)
            return (literal, "text=");

        var detailed = ExtractSpanDetailed(store, session, args, ResolveBuf(store, session, args));
        return (detailed.Text, detailed.From);
    }

    /// <summary>
    /// Span for <c>take</c>: whole buffer by default; else anchor / lines / sniper hold (same as copy).
    /// </summary>
    internal static TakeSpan ResolveTakeSpan(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var hasSpan = OptString(args, "anchor") is { Length: > 0 }
            || OptString(args, "at") is { Length: > 0 }
            || OptString(args, "from") is { Length: > 0 }
            || IntOrNull(args, "start_line") is not null
            || OptString(args, "text") is { Length: > 0 };

        if (!hasSpan
            && BoolOr(args, "sniper", false)
            && EditSniper.TryGetHold(out var sp, out var label, out var ls, out var cs, out var le, out var ce))
        {
            var sbuf = store.Resolve(sp, null);
            var start = OffsetOf(sbuf.Text, ls, cs);
            var end = OffsetOf(sbuf.Text, le, ce);
            if (end < start) (start, end) = (end, start);
            return new TakeSpan(sbuf, sbuf.Text[start..end], label, ls, cs, le, ce);
        }

        if (!hasSpan)
        {
            var whole = ResolveBuf(store, session, args);
            var lines = CountLines(whole.Text);
            var endCol = LineLengthAt(whole.Text, lines) + 1;
            return new TakeSpan(whole, whole.Text, WireFile(session, whole.Path), 1, 1, lines, endCol);
        }

        // text= alone (no buffer span) — ephemeral body, still need a buffer for path/fence if open
        if (OptString(args, "text") is { Length: > 0 } literal
            && OptString(args, "anchor") is null
            && OptString(args, "at") is null
            && OptString(args, "from") is null
            && IntOrNull(args, "start_line") is null)
        {
            DocBuffer? open = null;
            try { open = ResolveBuf(store, session, args); } catch { /* optional */ }
            if (open is null)
                throw new ArgumentException("take text= needs an open buffer or path= for fence/verify context.");
            return new TakeSpan(open, literal, "text=", 1, 1, 1, 1);
        }

        var buf = ResolveBuf(store, session, args);
        var wire = OptString(args, "anchor") ?? OptString(args, "at") ?? OptString(args, "from");
        if (wire is { Length: > 0 })
        {
            var span = BracketLocate.Parse(wire);
            if (span.File is { Length: > 0 })
            {
                var file = ResolveUserPath(session, span.File);
                if (!string.Equals(file, buf.Path, StringComparison.OrdinalIgnoreCase))
                    buf = store.Resolve(file, null);
            }
        }

        var d = ExtractSpanDetailed(store, session, args, buf);
        return new TakeSpan(buf, d.Text, d.From, d.StartLine, d.StartCol, d.EndLine, d.EndCol);
    }

    internal readonly record struct TakeSpan(
        DocBuffer Buf,
        string Body,
        string From,
        int StartLine,
        int StartCol,
        int EndLine,
        int EndCol);

    static (string Text, string From, int StartLine, int StartCol, int EndLine, int EndCol) ExtractSpanDetailed(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        DocBuffer buf)
    {
        var wire = OptString(args, "anchor") ?? OptString(args, "at") ?? OptString(args, "from");
        if (wire is { Length: > 0 })
        {
            var span = BracketLocate.Parse(wire);
            if (span.File is { Length: > 0 })
            {
                var file = ResolveUserPath(session, span.File);
                if (!string.Equals(file, buf.Path, StringComparison.OrdinalIgnoreCase))
                    buf = store.Resolve(file, null);
            }

            if (!BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, span, out var range, out var detail))
                throw new ArgumentException($"Copy/cut anchor resolve failed ({detail}): {wire}");
            var start = OffsetOf(buf.Text, range.LineStart, range.ColumnStart);
            var end = OffsetOf(buf.Text, range.LineEnd, range.ColumnEnd);
            if (end < start)
                (start, end) = (end, start);
            var text = buf.Text[start..end];
            return (text, NormalizeWire(wire), range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd);
        }

        if (IntOrNull(args, "start_line") is int sl)
        {
            var sc = IntOrNull(args, "start_column") ?? 1;
            var el = IntOrNull(args, "end_line") ?? sl;
            var ec = IntOrNull(args, "end_column");
            if (ec is null)
            {
                // whole lines start..end inclusive
                var lineStart = OffsetOf(buf.Text, sl, 1);
                var lineEnd = el >= CountLines(buf.Text)
                    ? buf.Text.Length
                    : OffsetOf(buf.Text, el + 1, 1);
                var text = buf.Text[lineStart..lineEnd];
                var endCol = LineLengthAt(buf.Text, el) + 1;
                return (text, WireLines(session, buf.Path, sl, el), sl, 1, el, endCol);
            }

            var s = OffsetOf(buf.Text, sl, sc);
            var e = OffsetOf(buf.Text, el, ec.Value);
            if (e < s)
                (s, e) = (e, s);
            return (buf.Text[s..e], WireLines(session, buf.Path, sl, el), sl, sc, el, ec.Value);
        }

        if (OptString(args, "text") is { Length: > 0 } literal)
            return (literal, "text=", 1, 1, 1, 1);

        throw new ArgumentException("copy/cut needs anchor= or start_line=/end_line= (or text= for clipboard only).");
    }

    static object HitCard(SessionContext session, DocBuffer buf, int index, int length, string match)
    {
        var (line, col) = LineColAt(buf.Text, index);
        var endIndex = index + Math.Max(0, length);
        var (endLine, endCol) = LineColAt(buf.Text, Math.Min(buf.Text.Length, endIndex));
        var preview = match.Length > 80 ? match[..80] + "…" : match;
        preview = preview.Replace("\r", "").Replace("\n", "⏎");
        return new
        {
            anchor = BracketLocate.Format(new BracketLocate.Span(
                FileLabel(session, buf.Path),
                null,
                line,
                endLine == line ? null : endLine)),
            line,
            column = col,
            end_line = endLine,
            end_column = endCol,
            preview
        };
    }

}
