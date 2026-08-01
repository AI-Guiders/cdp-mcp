using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class EditorComfort
{
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
                    new { go = "complete", label = "Completions at hit", why = "line/column from hits[0] → get_completions" },
                    new { go = "signature_help", label = "Signature help", why = "inside call near hit" },
                    new { go = "peek", label = "Peek hit", why = "go_args.wire= from hits[].anchor" },
                    new { go = "replace_all", label = "Replace all", why = "query= + text= replacement" },
                    new { go = "find_in_files", label = "Find in Files", why = "scope=project same query" }
                ]
                : (object[])
                [
                    new { go = "find", label = "Retry", why = "regex=true / scope=project" }
                ],
            hint =
                "VS Find (buffer). Bare verb find|get_find. regex=true = Use Regular Expressions. " +
                "scope=project|files = Find in Files (rg → anchors). next: complete/signature_help."
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

}
