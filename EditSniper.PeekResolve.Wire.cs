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
    static bool TryResolveTextNeedle(string text, string needle, int? lineHint, out BracketSyntaxResolve.TextRange range, out string detail, out string error)
    {
        range = default!;
        detail = "";
        error = "";
        needle = BracketLocate.SanitizeTextNeedle(needle);
        if (string.IsNullOrWhiteSpace(needle))
        {
            error = "text_needle_empty";
            return false;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lineHint is int hint && hint >= 1 && hint <= lines.Length && lines[hint - 1].Contains(needle, StringComparison.Ordinal))
        {
            range = ExpandToFullLines(text, new BracketSyntaxResolve.TextRange(hint, 1, hint, 1));
            detail = "content_literal+line_hint";
            return true;
        }

        var hits = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
                hits.Add(i + 1);
        }

        if (hits.Count == 0)
        {
            error = "text_needle_not_found";
            return false;
        }

        if (hits.Count > 1 && lineHint is null)
        {
            error = $"text_needle_ambiguous:{hits.Count}";
            return false;
        }

        var line = lineHint is int h ? hits.OrderBy(x => Math.Abs(x - h)).First() : hits[0];
        range = ExpandToFullLines(text, new BracketSyntaxResolve.TextRange(line, 1, line, 1));
        detail = hits.Count == 1 ? "content_literal" : "content_literal+nearest";
        return true;
    }

    static bool TryResolveWire(DocumentBufferStore store, SessionContext session, string wire, out string path, out BracketSyntaxResolve.TextRange range, out string detail, out string error)
    {
        path = "";
        range = default!;
        detail = "";
        error = "";
        BracketLocate.Span span;
        try
        {
            span = BracketLocate.Parse(wire);
        }
        catch (Exception ex)
        {
            error = $"anchor_parse:{ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(span.File))
        {
            error = "F_required";
            return false;
        }

        path = ResolveUserPath(session, span.File);
        // Content needle: re-find by text (survives L-drift). Optional L: is a hint only.
        if (!string.IsNullOrWhiteSpace(span.TextNeedle))
        {
            var contentText = ReadText(store, path);
            if (!TryResolveTextNeedle(contentText, span.TextNeedle, span.LineStart, out range, out detail, out error))
                return false;
            return true;
        }

        // L-only corridor: literal lines (honor L:a-b). Avoid Roslyn node-snap / XML-as-C# drift.
        if (IsLineOnlyCorridor(span))
        {
            var lineText = ReadText(store, path);
            var ls = span.LineStart!.Value;
            var le = span.LineEnd ?? ls;
            range = ExpandToFullLines(lineText, new BracketSyntaxResolve.TextRange(ls, 1, le, 1));
            detail = "line_literal";
            return true;
        }

        var family = BracketLocate.ClassifyFamily(span, out var familyError);
        if (familyError is not null)
        {
            error = familyError;
            return false;
        }

        if (family != BracketLocate.AxisFamily.Csharp)
        {
            error = "csharp_axes_required";
            return false;
        }

        var text = ReadText(store, path);
        if (!BracketSyntaxResolve.TryResolve(path, text, span, out range, out detail))
        {
            error = $"anchor_resolve:{detail}";
            return false;
        }

        return true;
    }
}