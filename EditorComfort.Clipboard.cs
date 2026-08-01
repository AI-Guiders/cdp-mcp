using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class EditorComfort
{
    static string ClipboardCard(IReadOnlyDictionary<string, JsonElement> args)
    {
        var dropFrame = OptString(args, "frame") ?? OptString(args, "id");
        if (BoolOr(args, "clear", false))
        {
            if (dropFrame is { Length: > 0 })
            {
                if (!SessionClipboard.Drop(dropFrame, out var dropped))
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "clipboard",
                        error = "frame_not_found",
                        frame = dropFrame
                    }, Pretty);
                }

                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = true,
                    op = "clipboard",
                    dropped = dropped,
                    clipboard = SessionClipboard.SceneCard(),
                    next = new object[]
                    {
                        new { go = "clipboard", label = "Clipboard", why = "remaining frames" },
                        new { go = "copy", label = "Copy", why = "push frame" }
                    },
                    hint = $"Dropped frame {dropped}."
                }, Pretty);
            }

            return ClipboardClear();
        }

        var scene = SessionClipboard.SceneCard();
        var empty = !SessionClipboard.Any();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "clipboard",
            clipboard = scene,
            next = empty
                ? (object[])
                [
                    new { go = "copy", label = "Copy into clip", why = "anchor= → new frame" },
                    new { go = "cut", label = "Cut into clip", why = "anchor= — frame + remove" }
                ]
                : (object[])
                [
                    new { go = "paste", label = "Paste current", why = "frame= omit = MRU; place=before|after|sniper" },
                    new { go = "paste", label = "Paste frame", why = "frame=cN preserve=false if one-shot" },
                    new { go = "clip_clear", label = "Clear all", why = "drop every frame" }
                ],
            hint =
                "Android-style IDE clipboard: frames c1… . go=paste frame=cN. " +
                "preserve=true (default) keeps frame; preserve=false burns after paste. " +
                "clear=true frame=cN drops one; clip_clear drops all."
        }, Pretty);
    }

    static string ClipboardClear()
    {
        SessionClipboard.ClearAll();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "clipboard_clear",
            empty = true,
            count = 0,
            next = new object[]
            {
                new { go = "copy", label = "Copy", why = "push frame" },
                new { go = "cut", label = "Cut", why = "push + remove" }
            },
            hint = "All clipboard frames cleared."
        }, Pretty);
    }

    static string Undo(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var buf = ResolveBuf(store, session, args);
        lock (Gate)
        {
            var stack = StackFor(buf.Path);
            if (stack.Undo.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "undo",
                    error = "nothing_to_undo",
                    hint = "No edit stack for this buffer yet."
                }, Pretty);
            }

            var (prev, label) = stack.Undo.Pop();
            stack.Redo.Push((buf.Text, "redo-of:" + label));
            ApplyRestore(store, buf, prev, BoolOr(args, "flush", true));
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "undo",
                undone = label,
                undo_left = stack.Undo.Count,
                redo_left = stack.Redo.Count,
                meta = buf.ToMeta(),
                next = ComfortNext(buf),
                hint = "Restored previous buffer text. go=redo to reverse."
            }, Pretty);
        }
    }

    static string Redo(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var buf = ResolveBuf(store, session, args);
        lock (Gate)
        {
            var stack = StackFor(buf.Path);
            if (stack.Redo.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "redo",
                    error = "nothing_to_redo"
                }, Pretty);
            }

            var (next, label) = stack.Redo.Pop();
            stack.Undo.Push((buf.Text, "undo-of:" + label));
            ApplyRestore(store, buf, next, BoolOr(args, "flush", true));
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "redo",
                redone = label,
                undo_left = stack.Undo.Count,
                redo_left = stack.Redo.Count,
                meta = buf.ToMeta(),
                next = ComfortNext(buf),
                hint = "Re-applied. go=undo to reverse."
            }, Pretty);
        }
    }

    static string History(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var buf = ResolveBuf(store, session, args);
        lock (Gate)
        {
            var stack = StackFor(buf.Path);
            var undo = stack.Undo.Take(MaxHistoryCards).Select((e, i) => new
            {
                i,
                label = e.Label,
                chars = e.Text.Length
            }).ToArray();
            var redo = stack.Redo.Take(MaxHistoryCards).Select((e, i) => new
            {
                i,
                label = e.Label,
                chars = e.Text.Length
            }).ToArray();
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "history",
                file = FileLabel(session, buf.Path),
                undo_count = stack.Undo.Count,
                redo_count = stack.Redo.Count,
                undo,
                redo,
                clipboard = ClipSummary(),
                nav = NavPulse(),
                next = ComfortNext(buf),
                hint = "go=undo / go=redo. Labels are edit ops that mutated this buffer."
            }, Pretty);
        }
    }

    /// <summary>
    /// Dump draft in one shot (Cursor Write analogue): path= file, or sniper/anchor destination.
    /// Body from text=/body= or frame= (clipboard). Then refine with edit/paste.
    /// </summary>
}
