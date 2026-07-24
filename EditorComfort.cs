using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Basic editor comfort humans forget to ask for: undo/redo, clipboard, locus back/forward,
/// find/replace, recent files, scratch. Anchors on the surface — not path archaeology.
/// </summary>
internal static class EditorComfort
{
    public const string Schema = "editor_comfort/v0";
    public const int MaxUndo = 64;
    public const int MaxFindHits = 80;
    public const int MaxRecent = 24;
    public const int MaxHistoryCards = 20;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly object Gate = new();
    static readonly Dictionary<string, EditStack> Stacks = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> RecentPaths = [];
    static readonly List<string> NavBack = [];
    static readonly List<string> NavForward = [];
    static string? NavCurrent;
    static int ScratchSeq;

    sealed class EditStack
    {
        public readonly Stack<(string Text, string Label)> Undo = new();
        public readonly Stack<(string Text, string Label)> Redo = new();
    }

    public static bool IsComfortOp(string op) => op is
        "undo" or "redo" or "history"
        or "copy" or "paste" or "cut"
        or "clipboard" or "clip" or "clipboard_clear" or "clip_clear"
        or "find" or "find_all" or "replace_all"
        or "back" or "forward" or "nav" or "nav_status"
        or "recent_files" or "scratch";

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        string op,
        IReadOnlyDictionary<string, JsonElement> args) =>
        op switch
        {
            "undo" => Undo(store, session, args),
            "redo" => Redo(store, session, args),
            "history" => History(store, session, args),
            "copy" => Copy(store, session, args),
            "cut" => Cut(store, session, args),
            "paste" => Paste(store, session, args),
            "clipboard" or "clip" => ClipboardCard(args),
            "clipboard_clear" or "clip_clear" => ClipboardClear(),
            "find" or "find_all" => Find(store, session, args, all: op == "find_all" || BoolOr(args, "all", false)),
            "replace_all" => ReplaceAll(store, session, args),
            "back" => NavStep(store, session, forward: false),
            "forward" => NavStep(store, session, forward: true),
            "nav" or "nav_status" => NavStatus(),
            "recent_files" => RecentFilesCard(session),
            "scratch" => Scratch(store, session, args),
            _ => JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "unknown_comfort_op",
                op
            }, Pretty)
        };

    /// <summary>Call after a successful buffer mutate (pre-text → post-text).</summary>
    public static void RecordEdit(string absolutePath, string beforeText, string afterText, string label)
    {
        if (string.Equals(beforeText, afterText, StringComparison.Ordinal))
            return;
        lock (Gate)
        {
            var stack = StackFor(absolutePath);
            stack.Undo.Push((beforeText, label));
            if (stack.Undo.Count > MaxUndo)
            {
                var keep = stack.Undo.Take(MaxUndo).Reverse().ToArray();
                stack.Undo.Clear();
                foreach (var x in keep)
                    stack.Undo.Push(x);
            }

            stack.Redo.Clear();
        }

        RememberFile(absolutePath);
    }

    public static void ClearStack(string absolutePath)
    {
        lock (Gate)
            Stacks.Remove(absolutePath);
    }

    public static void RememberFile(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return;
        var full = Path.GetFullPath(absolutePath);
        lock (Gate)
        {
            RecentPaths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            RecentPaths.Insert(0, full);
            while (RecentPaths.Count > MaxRecent)
                RecentPaths.RemoveAt(RecentPaths.Count - 1);
        }
    }

    public static void PushLocus(SessionContext session, string? wireOrPath)
    {
        if (string.IsNullOrWhiteSpace(wireOrPath))
            return;
        var wire = NormalizeWireOrFile(session, wireOrPath!);
        lock (Gate)
        {
            if (string.Equals(NavCurrent, wire, StringComparison.Ordinal))
                return;
            if (NavCurrent is { Length: > 0 })
                NavBack.Add(NavCurrent);
            while (NavBack.Count > 64)
                NavBack.RemoveAt(0);
            NavCurrent = wire;
            NavForward.Clear();
        }
    }

    public static object Snap()
    {
        lock (Gate)
        {
            return new
            {
                undo_buffers = Stacks.Count(kv => kv.Value.Undo.Count > 0),
                clipboard = SessionClipboard.Summary(),
                nav_back = NavBack.Count,
                nav_forward = NavForward.Count,
                nav_current = NavCurrent,
                recent_files = RecentPaths.Count
            };
        }
    }

    public static bool AnyUndo()
    {
        lock (Gate)
            return Stacks.Values.Any(s => s.Undo.Count > 0);
    }

    public static bool AnyNavBack()
    {
        lock (Gate)
            return NavBack.Count > 0;
    }

    public static bool AnyClipboard() => SessionClipboard.Any();

    /// <summary>Desk locus pulse when clipboard holds frames.</summary>
    public static (int Count, string? CurrentId, int Chars, string? From, string Preview)? ClipboardLocusDetail() =>
        SessionClipboard.LocusPulse();

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

    static string Copy(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var (text, from) = ExtractSpan(store, session, args);
        var frame = SessionClipboard.Push(text, from, "copy");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "copy",
            frame = frame.Id,
            chars = text.Length,
            from,
            clipboard = SessionClipboard.Summary(),
            next = new object[]
            {
                new { go = "paste", label = "Paste frame", why = $"frame={frame.Id} place=after|before|sniper" },
                new { go = "clipboard", label = "Clipboard", why = "Android-style frame list" },
                new { go = "cut", label = "Cut instead", why = "same span → frame + remove" }
            },
            hint = $"Pushed frame {frame.Id} (MRU). Paste keeps it unless preserve=false."
        }, Pretty);
    }

    static string Cut(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (OptString(args, "text") is { Length: > 0 }
            && OptString(args, "anchor") is null
            && OptString(args, "at") is null
            && OptString(args, "from") is null
            && IntOrNull(args, "start_line") is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "cut",
                error = "cut_needs_span",
                hint = "cut needs anchor= or start_line= (text= alone is copy-only)"
            }, Pretty);
        }

        var buf = ResolveBuf(store, session, args);
        var (text, from, startLine, startCol, endLine, endCol) = ExtractSpanDetailed(store, session, args, buf);
        var frame = SessionClipboard.Push(text, from, "cut");

        var before = buf.Text;
        store.ApplyReplaceRange(buf, startLine, startCol, endLine, endCol, "");
        var flush = BoolOr(args, "flush", true);
        if (flush)
            store.Flush(buf, allowShrink: true);
        RecordEdit(buf.Path, before, buf.Text, "cut");
        PushLocus(session, from);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "cut",
            frame = frame.Id,
            chars = text.Length,
            from,
            meta = buf.ToMeta(),
            clipboard = SessionClipboard.Summary(),
            next = new object[]
            {
                new { go = "paste", label = "Paste frame", why = $"frame={frame.Id}" },
                new { go = "undo", label = "Undo cut", why = "restore" },
                new { go = "clipboard", label = "Clipboard", why = "frames" }
            },
            hint = $"Cut → frame {frame.Id} + removed from buffer."
        }, Pretty);
    }

    static string Paste(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var frameArg = OptString(args, "frame") ?? OptString(args, "id") ?? OptString(args, "clip");
        var preserve = BoolOr(args, "preserve", true);
        string text;
        string? frameId;

        if (OptString(args, "text") is { Length: > 0 } overrideText
            && frameArg is null)
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
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "paste",
                    error = SessionClipboard.Any() ? "frame_not_found" : "clipboard_empty",
                    frame = frameArg,
                    hint = "go=copy / go=cut first; or frame=cN from go=clipboard"
                }, Pretty);
            }

            text = frame.Text;
            frameId = frame.Id;
        }

        var place = (OptString(args, "place") ?? OptString(args, "at_place") ?? "after")
            .Trim()
            .ToLowerInvariant();
        if (place is "before" or "pre" or "b")
            place = "before";
        else if (place is "after" or "post" or "a")
            place = "after";
        else if (place is "replace" or "over" or "r")
            place = "replace";
        else if (place is "sniper" or "hold" or "target")
            place = "sniper";

        var useSniper = BoolOr(args, "sniper", false)
            || place == "sniper"
            || string.Equals(OptString(args, "dest"), "sniper", StringComparison.OrdinalIgnoreCase);

        DocBuffer buf;
        string where;
        var before = "";

        if (useSniper)
        {
            if (!EditSniper.TryGetHold(out var path, out var label, out var ls, out var cs, out var le, out var ce))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "paste",
                    error = "no_sniper_hold",
                    hint = "go=scope from=/till= first, then paste sniper=true place=before|after|replace"
                }, Pretty);
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
                var file = span.File is { Length: > 0 }
                    ? ResolveUserPath(session, span.File)
                    : buf.Path;
                if (!string.Equals(file, buf.Path, StringComparison.OrdinalIgnoreCase))
                    buf = store.Resolve(file, null);
                before = buf.Text;
                if (!BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, span, out var range, out var detail))
                    throw new ArgumentException($"Paste anchor resolve failed ({detail}): {wire}");
                where = ApplyPlaced(
                    store, session, buf, text, place is "replace" ? "replace" : place,
                    range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd,
                    FileLabel(session, buf.Path));
            }
            else if (IntOrNull(args, "start_line") is int sl)
            {
                buf = ResolveBuf(store, session, args);
                before = buf.Text;
                var sc = IntOrNull(args, "start_column") ?? 1;
                var el = IntOrNull(args, "end_line") ?? sl;
                var ec = IntOrNull(args, "end_column") ?? sc;
                where = ApplyPlaced(
                    store, session, buf, text, place is "replace" ? "replace" : place,
                    sl, sc, el, ec, FileLabel(session, buf.Path));
            }
            else
            {
                buf = ResolveBuf(store, session, args);
                before = buf.Text;
                // Append at end of file.
                var lines = CountLines(buf.Text);
                var lastLen = LastLineLength(buf.Text);
                store.ApplyReplaceRange(
                    buf, lines, Math.Max(1, lastLen + 1), lines, Math.Max(1, lastLen + 1), text);
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

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "paste",
            frame = frameId,
            preserved = frameId is not null && preserve,
            chars = text.Length,
            place = useSniper ? $"sniper:{(place is "before" or "after" or "replace" ? place : "before")}" : place,
            at = where,
            meta = buf.ToMeta(),
            clipboard = SessionClipboard.Summary(),
            next = ComfortNext(buf),
            hint = frameId is null
                ? "Pasted text= override."
                : preserve
                    ? $"Pasted frame {frameId} (still in clipboard). preserve=false to burn."
                    : $"Pasted and dropped frame {frameId}."
        }, Pretty);
    }

    /// <summary>Insert before/after range or replace it.</summary>
    static string ApplyPlaced(
        DocumentBufferStore store,
        SessionContext session,
        DocBuffer buf,
        string text,
        string place,
        int lineStart,
        int colStart,
        int lineEnd,
        int colEnd,
        string fileLabel)
    {
        if (place == "replace")
        {
            store.ApplyReplaceRange(buf, lineStart, colStart, lineEnd, colEnd, text);
            return BracketLocate.Format(new BracketLocate.Span(
                fileLabel, null, lineStart, lineEnd == lineStart ? null : lineEnd));
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

    static string Find(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool all)
    {
        var scopeRaw = OptString(args, "scope") ?? OptString(args, "in") ?? "buffer";
        if (FindInFiles.IsFilesScope(scopeRaw))
            return FindInFiles.Dispatch(store, session, args, all);

        var buf = ResolveBuf(store, session, args);
        var query = OptString(args, "query") ?? OptString(args, "text") ?? OptString(args, "pattern");
        if (string.IsNullOrEmpty(query))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = "buffer",
                error = "query_required",
                hint = "query= (buffer Find). regex=true = Use Regular Expressions. scope=project = Find in Files."
            }, Pretty);
        }

        var regex = BoolOr(args, "regex", false);
        var ignoreCase = BoolOr(args, "ignore_case", true);
        RememberFile(buf.Path);
        PushLocus(session, WireFile(session, buf.Path));

        var hits = new List<object>();
        if (regex)
        {
            var opts = RegexOptions.Multiline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            foreach (Match m in Regex.Matches(buf.Text, query!, opts))
            {
                hits.Add(HitCard(session, buf, m.Index, m.Length, m.Value));
                if (!all || hits.Count >= MaxFindHits)
                    break;
            }
        }
        else
        {
            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var start = 0;
            while (start < buf.Text.Length)
            {
                var idx = buf.Text.IndexOf(query!, start, cmp);
                if (idx < 0)
                    break;
                hits.Add(HitCard(session, buf, idx, query!.Length, buf.Text.Substring(idx, query.Length)));
                start = idx + Math.Max(1, query.Length);
                if (!all || hits.Count >= MaxFindHits)
                    break;
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = all ? "find_all" : "find",
            scope = "buffer",
            query,
            regex,
            ignore_case = ignoreCase,
            count = hits.Count,
            truncated = all && hits.Count >= MaxFindHits,
            hits,
            next = hits.Count > 0
                ? (object[])
                [
                    new { go = "peek", label = "Peek hit", why = "go_args.wire= from hits[].anchor" },
                    new { go = "replace_all", label = "Replace all", why = "query= + text= replacement" },
                    new { go = "find_all", label = "Find in Files", why = "scope=project same query" }
                ]
                : (object[])
                [
                    new { go = "find", label = "Retry", why = "regex=true / scope=project" }
                ],
            hint =
                "VS Find (buffer). regex=true = Use Regular Expressions. " +
                "scope=project|files = Find in Files (rg → anchors)."
        }, Pretty);
    }

    static string ReplaceAll(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var buf = ResolveBuf(store, session, args);
        var query = OptString(args, "query") ?? OptString(args, "old_string") ?? OptString(args, "pattern");
        var replacement = OptString(args, "text") ?? OptString(args, "new_string") ?? "";
        if (string.IsNullOrEmpty(query))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "replace_all",
                error = "query_required"
            }, Pretty);
        }

        var regex = BoolOr(args, "regex", false);
        var ignoreCase = BoolOr(args, "ignore_case", false);
        var before = buf.Text;
        string after;
        int count;
        if (regex)
        {
            var opts = RegexOptions.Multiline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            var rx = new Regex(query!, opts);
            count = rx.Matches(before).Count;
            after = rx.Replace(before, replacement);
        }
        else
        {
            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            count = 0;
            var sb = new StringBuilder();
            var start = 0;
            while (start < before.Length)
            {
                var idx = before.IndexOf(query!, start, cmp);
                if (idx < 0)
                {
                    sb.Append(before.AsSpan(start));
                    break;
                }

                sb.Append(before.AsSpan(start, idx - start));
                sb.Append(replacement);
                start = idx + query!.Length;
                count++;
            }

            after = sb.ToString();
        }

        if (count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "replace_all",
                replaced = 0,
                hint = "No matches."
            }, Pretty);
        }

        store.ApplySetText(buf, after);
        var flush = BoolOr(args, "flush", true);
        if (flush)
            store.Flush(buf, allowShrink: true);
        RecordEdit(buf.Path, before, after, $"replace_all×{count}");
        RememberFile(buf.Path);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "replace_all",
            replaced = count,
            meta = buf.ToMeta(),
            next = ComfortNext(buf),
            hint = "One undo step for the whole replace_all."
        }, Pretty);
    }

    static string NavStep(DocumentBufferStore store, SessionContext session, bool forward)
    {
        string? target;
        lock (Gate)
        {
            if (forward)
            {
                if (NavForward.Count == 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "forward",
                        error = "nav_empty"
                    }, Pretty);
                }

                if (NavCurrent is { Length: > 0 })
                    NavBack.Add(NavCurrent);
                target = NavForward[^1];
                NavForward.RemoveAt(NavForward.Count - 1);
                NavCurrent = target;
            }
            else
            {
                if (NavBack.Count == 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schema = Schema,
                        ok = false,
                        op = "back",
                        error = "nav_empty"
                    }, Pretty);
                }

                if (NavCurrent is { Length: > 0 })
                    NavForward.Add(NavCurrent);
                target = NavBack[^1];
                NavBack.RemoveAt(NavBack.Count - 1);
                NavCurrent = target;
            }
        }

        // Best-effort: open file from F: if present.
        try
        {
            var span = BracketLocate.Parse(target);
            if (span.File is { Length: > 0 })
            {
                var path = ResolveUserPath(session, span.File);
                if (File.Exists(path))
                {
                    store.Open(path);
                    RememberFile(path);
                }
            }
        }
        catch
        {
            // wire may be bare path
            try
            {
                var path = ResolveUserPath(session, target.Trim('[', ']'));
                if (File.Exists(path))
                    store.Open(path);
            }
            catch
            {
                // ignore
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = forward ? "forward" : "back",
            locus = target,
            nav = NavPulse(),
            next = new object[]
            {
                new { go = "peek", label = "Peek locus", why = $"go_args.wire={target}" },
                new { go = forward ? "back" : "forward", label = forward ? "Back" : "Forward", why = "nav stack" }
            },
            hint = "Locus stack (VS Navigate Backward/Forward analogue)."
        }, Pretty);
    }

    static string NavStatus() =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "nav",
            nav = NavPulse(),
            hint = "go=back / go=forward"
        }, Pretty);

    static string RecentFilesCard(SessionContext session)
    {
        List<string> paths;
        lock (Gate)
            paths = RecentPaths.ToList();

        var files = paths.Select(p => new
        {
            anchor = WireFile(session, p),
            name = Path.GetFileName(p)
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "recent_files",
            count = files.Length,
            files,
            next = files.Length > 0
                ? new object[] { new { go = "buffer_scene", label = "Open from MRU", why = "cdp_buffer op=open via F:" } }
                : Array.Empty<object>(),
            hint = "MRU of edited/opened files this MCP session."
        }, Pretty);
    }

    static string Scratch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var root = session.ProjectRoot is { Length: > 0 } pr
            ? Path.Combine(pr, ".cdp", "scratch")
            : Path.Combine(Path.GetTempPath(), "cdp-scratch");
        Directory.CreateDirectory(root);
        int n;
        lock (Gate)
            n = ++ScratchSeq;
        var ext = OptString(args, "ext") ?? "cs";
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        var path = Path.Combine(root, $"untitled-{n}{ext}");
        var text = OptString(args, "text") ?? "// scratch\n";
        var buf = store.Create(path, text, overwrite: true);
        RememberFile(path);
        var wire = WireFile(session, path);
        PushLocus(session, wire);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "scratch",
            anchor = wire,
            meta = buf.ToMeta(),
            next = new object[]
            {
                new { go = "edit_draft", label = "Edit scratch", why = "untitled buffer ready" }
            },
            hint = "Untitled under .cdp/scratch (or temp). Not OS temp forever when project open."
        }, Pretty);
    }

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
            preview
        };
    }

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
            _ => defaultValue
        };
    }
}
