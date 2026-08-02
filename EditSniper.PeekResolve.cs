using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

internal static partial class EditSniper
{
    static string Peek(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (Hold is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                op = "peek",
                error = "no_scope",
                hint = "go=scope from=… first (or peek after target)"
            }, Pretty);
        }

        var h = Hold;
        var pad = Math.Clamp(IntOr(args, "pad", 0), 0, 40);
        var maxLines = Math.Clamp(IntOr(args, "max_lines", 60), 1, 200);
        var wire = OptString(args, "wire") ?? OptString(args, "anchor") ?? OptString(args, "at");

        int lineStart;
        int lineEnd;
        string? resolve = null;
        string? usedWire = null;

        if (!string.IsNullOrWhiteSpace(wire))
        {
            if (!TryResolveWire(store, session, wire, out var path, out var range, out var detail, out var err))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "peek",
                    error = err,
                    wire
                }, Pretty);
            }

            if (!string.Equals(path, h.Path, StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Serialize(new
                {
                    schema = Schema,
                    ok = false,
                    op = "peek",
                    error = "wire_outside_hold_file",
                    hold_path = h.Path,
                    wire_path = path
                }, Pretty);
            }

            lineStart = range.LineStart;
            lineEnd = range.LineEnd;
            resolve = detail;
            usedWire = NormalizeWire(wire);
        }
        else
        {
            lineStart = h.LineStart;
            lineEnd = h.LineEnd;
            usedWire = null;
        }

        lineStart = Math.Max(1, lineStart - pad);
        lineEnd += pad;

        var text = ReadText(store, h.Path);
        var allLines = SplitLines(text);
        lineEnd = Math.Min(allLines.Count, lineEnd);
        if (lineEnd - lineStart + 1 > maxLines)
            lineEnd = lineStart + maxLines - 1;

        var slice = new StringBuilder();
        for (var i = lineStart; i <= lineEnd; i++)
        {
            if (i > lineStart) slice.Append('\n');
            slice.Append(allLines[i - 1]);
        }

        var locusWire = usedWire
            ?? BracketLocate.Format(new BracketLocate.Span(
                h.FileLabel, null, lineStart, lineEnd == lineStart ? null : lineEnd));
        EditorComfort.PushLocus(session, locusWire);
        EditorComfort.RememberFile(h.Path);

        var peekBody = slice.ToString();
        Hold = h with { Phase = PhaseArmed, PeekText = peekBody };

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "peek",
            process = "verify",
            phase = PhaseArmed,
            hold = HoldCard(),
            wire = usedWire,
            resolve,
            start_line = lineStart,
            end_line = lineEnd,
            count = lineEnd - lineStart + 1,
            text = peekBody,
            next = ShootNext(),
            hint = "Peek re-arms. Fire hard-gated on phase=armed. pad= for ± lines."
        }, Pretty);
    }

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            list.Add(line);
        return list;
    }

    static object[] ShootNext() =>
        IsArmed
            ?
            [
                new { go = "put_sniper", label = "Fire put", why = "armed — text=/frame= into hold" },
                new { go = "paste_sniper", label = "Fire paste", why = "armed — clipboard → hold" },
                new { go = "target", label = "Outline corridor", why = "optional in-corridor nodes" },
                new { go = "peek", label = "Re-peek", why = "optional verify before fire" },
                new { go = "edit_draft", label = "Shoot (draft)", why = "mutate/fix plan" },
                new { go = "scope_clear", label = "Clear aim", why = "drop From/Till" }
            ]
            :
            [
                new { go = "scope", label = "Lock corridor", why = "from=/till= → auto-arm" },
                new { go = "scope_clear", label = "Clear aim", why = "drop From/Till" }
            ];

    static BracketSyntaxResolve.TextRange ExpandToFullLines(
        string text,
        BracketSyntaxResolve.TextRange zone)
    {
        var lines = SplitLines(text);
        if (lines.Count == 0)
            return new BracketSyntaxResolve.TextRange(1, 1, 1, 1);

        var ls = Math.Clamp(zone.LineStart, 1, lines.Count);
        var le = Math.Clamp(zone.LineEnd, ls, lines.Count);
        var endCol = Math.Max(1, lines[le - 1].Length + 1);
        return new BracketSyntaxResolve.TextRange(ls, 1, le, endCol);
    }

    static string SliceCorridor(string text, int lineStart, int lineEnd, int maxLines = 60)
    {
        var all = SplitLines(text);
        if (all.Count == 0)
            return "";
        lineStart = Math.Clamp(lineStart, 1, all.Count);
        lineEnd = Math.Clamp(lineEnd, lineStart, all.Count);
        if (lineEnd - lineStart + 1 > maxLines)
            lineEnd = lineStart + maxLines - 1;
        var sb = new StringBuilder();
        for (var i = lineStart; i <= lineEnd; i++)
        {
            if (i > lineStart) sb.Append('\n');
            sb.Append(all[i - 1]);
        }

        return sb.ToString();
    }

    static bool IsLineOnlyCorridor(BracketLocate.Span span) =>
        span.LineStart is not null
        && string.IsNullOrWhiteSpace(span.MemberKey)
        && string.IsNullOrWhiteSpace(span.ScopeKind)
        && string.IsNullOrWhiteSpace(span.TextNeedle)
        && string.IsNullOrWhiteSpace(span.Role)
        && string.IsNullOrWhiteSpace(span.XmlPath)
        && string.IsNullOrWhiteSpace(span.Attr);


}
