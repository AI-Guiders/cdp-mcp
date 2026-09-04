using System.Threading;
using AIGuiders.Platform.Execution.Language;
using AIGuiders.Platform.Modeling.Language;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// F# anchor resolver — symbol spans via the LRC FCS backend (GUIDERS-ADR-0021 port).
/// Axes: M = symbol name (module/type/let member; case-insensitive, first match in document order);
/// L = line_literal handled by the caller; T = text needle narrowed inside the resolved locus.
/// </summary>
internal static class FSharpAnchorResolve
{
    public static bool TryResolve(
        string path,
        string text,
        BracketLocate.Span span,
        out BracketSyntaxResolve.TextRange range,
        out string detail)
    {
        range = new BracketSyntaxResolve.TextRange(1, 1, 1, 1);
        detail = "";

        if (string.IsNullOrWhiteSpace(span.MemberKey))
        {
            // No member axis: whole-file locus.
            var allLines = text.Replace("\r\n", "\n").Split('\n');
            var lastLen = allLines.Length > 0 ? allLines[^1].Length : 0;
            range = new BracketSyntaxResolve.TextRange(1, 1, allLines.Length, Math.Max(1, lastLen + 1));
            detail = "fsharp:file";
            return true;
        }

        var backend = CdpLanguageResolverHost.Center.Resolve(path);
        if (backend is null)
        {
            detail = "no_lrc_backend";
            return false;
        }

        var req = new AIGuiders.Platform.Execution.Language.LanguageRequest(path, 1, 1, text);
        DocumentSymbolsResult symbols;
        try
        {
            symbols = backend
                .GetDocumentSymbolsAsync(req, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            detail = "fcs_failed:" + ex.Message;
            return false;
        }

        var wanted = span.MemberKey.Trim();
        var hit = FindSymbol(symbols.Root, wanted);
        if (hit is null)
        {
            detail = "symbol_not_found:" + wanted;
            return false;
        }

        var s = hit.Span;
        range = new BracketSyntaxResolve.TextRange(s.Line, s.Column, s.EndLine, Math.Max(1, s.EndColumn));
        detail = $"fcs:{hit.Kind}:{hit.Name}";

        if (!string.IsNullOrWhiteSpace(span.TextNeedle))
        {
            if (!TryNarrowToNeedle(text, range, span.TextNeedle, out range, out var needleDetail))
            {
                detail = needleDetail;
                return false;
            }

            detail += "+T";
        }

        return true;
    }

    private static LanguageSymbol? FindSymbol(LanguageSymbol root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase))
            return root;

        if (root.Children is null)
            return null;

        foreach (var child in root.Children)
        {
            var hit = FindSymbol(child, name);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    private static bool TryNarrowToNeedle(
        string text,
        BracketSyntaxResolve.TextRange range,
        string needle,
        out BracketSyntaxResolve.TextRange narrowed,
        out string detail)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var ln = range.LineStart; ln <= range.LineEnd && ln - 1 < lines.Length; ln++)
        {
            var idx = lines[ln - 1].IndexOf(needle, StringComparison.Ordinal);
            if (idx >= 0)
            {
                narrowed = new BracketSyntaxResolve.TextRange(ln, idx + 1, ln, idx + needle.Length + 1);
                detail = "needle";
                return true;
            }
        }

        narrowed = range;
        detail = "needle_not_found:" + needle;
        return false;
    }
}
