using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class EditorComfort
{
    static string Put(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var frameArg = OptString(args, "frame") ?? OptString(args, "id") ?? OptString(args, "clip");
        var preserve = BoolOr(args, "preserve", true);
        string text;
        string? frameId = null;
        var body = OptString(args, "text") ?? OptString(args, "body") ?? OptString(args, "content");

        if (body is { Length: > 0 })
        {
            text = body;
        }
        else if (frameArg is not null || SessionClipboard.Any())
        {
            var frame = SessionClipboard.Find(frameArg);
            if (frame is null)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "put",
                    error = SessionClipboard.Any() ? "frame_not_found" : "body_required",
                    hint = "text=/body= draft, or frame=cN from clipboard"
                }, Pretty);
            }

            text = frame.Text;
            frameId = frame.Id;
        }
        else
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "put",
                error = "body_required",
                hint = "text=/body= or frame=cN — dump draft in one shot"
            }, Pretty);
        }

        var place = (OptString(args, "place") ?? OptString(args, "at_place") ?? "replace")
            .Trim()
            .ToLowerInvariant();
        if (place is "before" or "pre" or "b")
            place = "before";
        else if (place is "after" or "post" or "a")
            place = "after";
        else if (place is "replace" or "over" or "r" or "into")
            place = "replace";
        else if (place is "sniper" or "hold" or "target")
            place = "sniper";

        var useSniper = BoolOr(args, "sniper", false)
            || place == "sniper"
            || string.Equals(OptString(args, "dest"), "sniper", StringComparison.OrdinalIgnoreCase);

        DocBuffer buf;
        string where;
        string mode;
        string? beforeText = null;

        if (useSniper)
        {
            if (!EditSniper.TryEnsureFire(out var fireErr, out var fireHint))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "put",
                    error = fireErr,
                    hint = fireHint
                }, Pretty);
            }

            if (!EditSniper.TryGetHold(out var path, out var label, out var ls, out var cs, out var le, out var ce))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "put",
                    error = "no_sniper_hold",
                    hint = "go=scope from=/till= then put sniper=true (default place=replace)"
                }, Pretty);
            }

            buf = store.Resolve(path, null);
            beforeText = buf.Text;
            var sniperPlace = place is "before" or "after" or "replace" ? place : "replace";
            where = ApplyPlaced(store, session, buf, text, sniperPlace, ls, cs, le, ce, label);
            mode = $"sniper:{sniperPlace}";
        }
        else if ((OptString(args, "anchor") ?? OptString(args, "at")) is { Length: > 0 } wire)
        {
            buf = ResolveBuf(store, session, args);
            var span = BracketLocate.Parse(wire);
            var file = span.File is { Length: > 0 }
                ? ResolveUserPath(session, span.File)
                : buf.Path;
            if (!string.Equals(file, buf.Path, StringComparison.OrdinalIgnoreCase))
                buf = store.Resolve(file, null);
            beforeText = buf.Text;
            if (!BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, span, out var range, out var detail))
                throw new ArgumentException($"Put anchor resolve failed ({detail}): {wire}");
            var p = place is "before" or "after" or "replace" ? place : "replace";
            where = ApplyPlaced(
                store, session, buf, text, p,
                range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd,
                FileLabel(session, buf.Path));
            mode = $"anchor:{p}";
        }
        else
        {
            // File dump (Cursor Write analogue).
            var pathArg = OptString(args, "path");
            if (pathArg is null)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "put",
                    error = "path_or_dest_required",
                    hint = "path= file dump | sniper=true | anchor= + place="
                }, Pretty);
            }

            var full = ResolveUserPath(session, pathArg);
            var overwrite = BoolOr(args, "overwrite", false);
            if (File.Exists(full) && !overwrite)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "put",
                    error = "file_exists",
                    path = full,
                    hint = "overwrite=true to replace draft, or pick another path="
                }, Pretty);
            }

            var existed = File.Exists(full);
            if (existed)
            {
                buf = store.Resolve(full, null);
                beforeText = buf.Text;
                store.ApplySetText(buf, text);
            }
            else
            {
                buf = store.Create(full, text, overwrite: false);
                beforeText = "";
            }

            where = WireFile(session, buf.Path);
            mode = existed ? "overwrite" : "create";
        }

        var flush = BoolOr(args, "flush", true);
        if (flush)
            store.Flush(buf, allowShrink: true);
        if (beforeText is not null)
            RecordEdit(buf.Path, beforeText, buf.Text, frameId is null ? $"put:{mode}" : $"put:{mode}:{frameId}");
        RememberFile(buf.Path);
        PushLocus(session, where);

        if (frameId is not null && !preserve)
            SessionClipboard.Drop(frameId, out _);

        object? land = null;
        try
        {
            var lines = SplitLinesLocal(buf.Text);
            var end = Math.Min(lines.Count, 12);
            var slice = string.Join('\n', lines.Take(end));
            if (slice.Length > 2_400)
                slice = slice[..2_400] + "\n…";
            land = new
            {
                anchor = where,
                doc_id = buf.DocId,
                start_line = 1,
                end_line = end,
                text = slice
            };
        }
        catch
        {
            /* peek optional */
        }

        DeskBookmark.Save(session, store);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "put",
            mode,
            frame = frameId,
            preserved = frameId is not null && preserve,
            chars = text.Length,
            at = where,
            land,
            meta = buf.ToMeta(),
            clipboard = SessionClipboard.Summary(),
            next = new object[]
            {
                new { go = "share", label = "Share with operator", why = "inbox + thin chat — not into agent" },
                new { go = "take", label = "Take into agent", why = "verify then chat_markdown — rare" },
                new { go = "scope", label = "Sniper refine", why = "from=/till= then edit" },
                new { go = "edit_draft", label = "Edit plan", why = "surgical slices" },
                new { go = "find", label = "Find in draft", why = "query= inside buffer" },
                new { go = "undo", label = "Undo put", why = "one stack step" }
            },
            hint =
                "Draft dumped. Verify+ship with take; refine with scope/edit/paste. " +
                "frame= from clipboard; preserve=false burns frame."
        }, Pretty);
    }

    static List<string> SplitLinesLocal(string text)
    {
        var list = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            list.Add(line);
        return list;
    }

}
