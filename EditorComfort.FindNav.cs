using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Buffer Find / ReplaceAll. Nav+MRU+scratch → <c>EditorComfort.FindNav.Nav.cs</c>.</summary>
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
}
