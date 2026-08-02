using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;
internal static partial class EditorComfort
{
    static string Cut(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (OptString(args, "text")is { Length: > 0 } && OptString(args, "anchor")is null && OptString(args, "at")is null && OptString(args, "from")is null && IntOrNull(args, "start_line")is null)
        {
            return JsonSerializer.Serialize(new { schema = Schema, ok = false, op = "cut", error = "cut_needs_span", hint = "cut needs anchor= or start_line= (text= alone is copy-only)" }, Pretty);
        }

        var buf = ResolveBuf(store, session, args);
        var(text, from, startLine, startCol, endLine, endCol) = ExtractSpanDetailed(store, session, args, buf);
        var frame = SessionClipboard.Push(text, from, "cut");
        var before = buf.Text;
        store.ApplyReplaceRange(buf, startLine, startCol, endLine, endCol, "");
        var flush = BoolOr(args, "flush", true);
        if (flush)
            store.Flush(buf, allowShrink: true);
        RecordEdit(buf.Path, before, buf.Text, "cut");
        PushLocus(session, from);
        return JsonSerializer.Serialize(new { schema = Schema, ok = true, op = "cut", frame = frame.Id, chars = text.Length, from, meta = buf.ToMeta(), clipboard = SessionClipboard.Summary(), next = new object[] { new { go = "paste", label = "Paste frame", why = $"frame={frame.Id}" }, new { go = "undo", label = "Undo cut", why = "restore" }, new { go = "clipboard", label = "Clipboard", why = "frames" } }, hint = $"Cut → frame {frame.Id} + removed from buffer." }, Pretty);
    }

    static string Paste(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var frameArg = OptString(args, "frame") ?? OptString(args, "id") ?? OptString(args, "clip");
        var preserve = BoolOr(args, "preserve", true);
        string text;
        string? frameId;
        if (OptString(args, "text")is { Length: > 0 } overrideText && frameArg is null)
        {
            // One-shot text paste without touching frames.
            text = overrideText;
            frameId = null;
        }
        else
        {
            var frame = SessionClipboard.Find(frameArg);
            if (frame is null)
            {
                return JsonSerializer.Serialize(new { schema = Schema, ok = false, op = "paste", error = SessionClipboard.Any() ? "frame_not_found" : "clipboard_empty", frame = frameArg, hint = "go=copy / go=cut first; or frame=cN from go=clipboard" }, Pretty);
            }

            text = frame.Text;
            frameId = frame.Id;
        }

        var place = (OptString(args, "place") ?? OptString(args, "at_place") ?? "after").Trim().ToLowerInvariant();
        if (place is "before" or "pre" or "b")
            place = "before";
        else if (place is "after" or "post" or "a")
            place = "after";
        else if (place is "replace" or "over" or "r")
            place = "replace";
        else if (place is "sniper" or "hold" or "target")
            place = "sniper";
        var useSniper = BoolOr(args, "sniper", false) || place == "sniper" || string.Equals(OptString(args, "dest"), "sniper", StringComparison.OrdinalIgnoreCase);
        DocBuffer buf;
        string where;
        var before = "";
        if (useSniper)
        {
            if (!EditSniper.TryEnsureFire(out var fireErr, out var fireHint))
            {
                return JsonSerializer.Serialize(new { schema = Schema, ok = false, op = "paste", error = fireErr, hint = fireHint }, Pretty);
            }

            if (!EditSniper.TryGetHold(out var path, out var label, out var ls, out var cs, out var le, out var ce))
            {
                return JsonSerializer.Serialize(new { schema = Schema, ok = false, op = "paste", error = "no_sniper_hold", hint = "go=scope from=/till= first, then paste sniper=true place=before|after|replace" }, Pretty);
            }

            buf = store.Resolve(path, null);
            before = buf.Text;
            var sniperPlace = place is "before" or "after" or "replace" ? place : "before";
            where = ApplyPlaced(store, session, buf, text, sniperPlace, ls, cs, le, ce, label);
        }
        else
        {
            var wire = OptString(args, "anchor") ?? OptString(args, "at") ?? OptString(args, "from");
            if (wire is { Length: > 0 })
            {
                buf = ResolveBuf(store, session, args);
                var span = BracketLocate.Parse(wire);
                var file = span.File is { Length: > 0 } ? ResolveUserPath(session, span.File) : buf.Path;
                if (!string.Equals(file, buf.Path, StringComparison.OrdinalIgnoreCase))
                    buf = store.Resolve(file, null);
                before = buf.Text;
                if (!BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, span, out var range, out var detail))
                    throw new ArgumentException($"Paste anchor resolve failed ({detail}): {wire}");
                where = ApplyPlaced(store, session, buf, text, place is "replace" ? "replace" : place, range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd, FileLabel(session, buf.Path));
            }
            else if (IntOrNull(args, "start_line")is int sl)
            {
                buf = ResolveBuf(store, session, args);
                before = buf.Text;
                var sc = IntOrNull(args, "start_column") ?? 1;
                var el = IntOrNull(args, "end_line") ?? sl;
                var ec = IntOrNull(args, "end_column") ?? sc;
                where = ApplyPlaced(store, session, buf, text, place is "replace" ? "replace" : place, sl, sc, el, ec, FileLabel(session, buf.Path));
            }
            else
            {
                buf = ResolveBuf(store, session, args);
                before = buf.Text;
                // Append at end of file.
                var lines = CountLines(buf.Text);
                var lastLen = LastLineLength(buf.Text);
                store.ApplyReplaceRange(buf, lines, Math.Max(1, lastLen + 1), lines, Math.Max(1, lastLen + 1), text);
                where = WireLines(session, buf.Path, lines, lines);
            }
        }

        var flush = BoolOr(args, "flush", true);
        if (flush)
            store.Flush(buf, allowShrink: true);
        RecordEdit(buf.Path, before, buf.Text, frameId is null ? "paste" : $"paste:{frameId}");
        PushLocus(session, where);
        if (frameId is not null && !preserve)
            SessionClipboard.Drop(frameId, out _);
        return JsonSerializer.Serialize(new { schema = Schema, ok = true, op = "paste", frame = frameId, preserved = frameId is not null && preserve, chars = text.Length, place = useSniper ? $"sniper:{(place is "before" or "after" or "replace" ? place : "before")}" : place, at = where, meta = buf.ToMeta(), clipboard = SessionClipboard.Summary(), next = ComfortNext(buf), hint = frameId is null ? "Pasted text= override." : preserve ? $"Pasted frame {frameId} (still in clipboard). preserve=false to burn." : $"Pasted and dropped frame {frameId}." }, Pretty);
    }

    /// <summary>Insert before/after range or replace it.</summary>
    static string ApplyPlaced(DocumentBufferStore store, SessionContext session, DocBuffer buf, string text, string place, int lineStart, int colStart, int lineEnd, int colEnd, string fileLabel)
    {
        if (place == "replace")
        {
            store.ApplyReplaceRange(buf, lineStart, colStart, lineEnd, colEnd, text);
            return BracketLocate.Format(new BracketLocate.Span(fileLabel, null, lineStart, lineEnd == lineStart ? null : lineEnd));
        }

        if (place == "before")
        {
            store.ApplyReplaceRange(buf, lineStart, colStart, lineStart, colStart, text);
            return BracketLocate.Format(new BracketLocate.Span(fileLabel, null, lineStart, null));
        }

        // after — insert at end of range (exclusive end point)
        store.ApplyReplaceRange(buf, lineEnd, colEnd, lineEnd, colEnd, text);
        return BracketLocate.Format(new BracketLocate.Span(fileLabel, null, lineEnd, null));
    }
}