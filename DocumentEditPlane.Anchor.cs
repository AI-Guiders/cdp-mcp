using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class DocumentEditPlane
{
    static object ApplyAnchorEdit(
        DocumentBufferStore store,
        SessionContext session,
        DocBuffer buf,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var wire = OptString(args, "anchor") ?? OptString(args, "at")
            ?? throw new ArgumentException("edit_op=anchor requires anchor= (or at=) bracket wire [F:;M:;K:] or [F:;X:;A:].");
        var replacement = OptString(args, "text") ?? OptString(args, "new_string")
            ?? throw new ArgumentException("edit_op=anchor requires text= (body for place=; default place=replace overwrites locus).");
        var place = NormalizeAnchorPlace(OptString(args, "place") ?? OptString(args, "at_place"));

        BracketLocate.Span span;
        try
        {
            span = BracketLocate.Parse(wire);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Anchor wire invalid: {ex.Message}");
        }

        var filePath = ResolveAnchorFilePath(buf, session, span, OptString(args, "path"));
        if (!string.Equals(filePath, buf.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Anchor F: resolves to '{filePath}' but edit gate/buffer is '{buf.Path}'. " +
                "Pass path= matching F: (or omit path and put absolute/relative path in F:).");
        }

        var family = BracketLocate.ClassifyFamily(span, out var familyError);
        if (familyError is not null)
            throw new ArgumentException(familyError);
        if (family == BracketLocate.AxisFamily.None)
            throw new ArgumentException("Anchor needs csharp axes (M/L/S/K) or xml axes (X/A).");
        if (family == BracketLocate.AxisFamily.Navigation)
            throw new ArgumentException(
                "Family:navigation is land-only — use cdp_land (not edit_op=anchor).");

        if (family == BracketLocate.AxisFamily.Csharp)
        {
            if (!string.Equals(buf.Language, "csharp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"axes_mismatch: csharp axes on language={buf.Language}. path={buf.Path}");

            if (!BracketSyntaxResolve.TryFindAttachTarget(buf.Path, buf.Text, span, out var target, out var detail))
                throw new ArgumentException($"Anchor resolve failed ({detail}): {wire}");

            BracketSyntaxResolve.TextRange range;
            if (!string.IsNullOrWhiteSpace(span.TextNeedle))
            {
                if (!BracketSyntaxResolve.TryNarrowRangeToTextNeedle(
                        target.Tree, target.Node, span.TextNeedle, out range, out var narrowDetail))
                    throw new ArgumentException($"Anchor resolve failed ({narrowDetail}): {wire}");
                detail = $"{target.Detail}+T";
            }
            else if (WantsBlockInteriorPlace(place, target.Node)
                     && BracketSyntaxResolve.TryGetBlockInteriorInsertPoint(
                         target.Node,
                         before: place is "before" or "into",
                         out range,
                         out var bodyDetail))
            {
                // Type/namespace / explicit into|end — inside braces.
                // Method M:+before|after is sibling (outside) — MergeGoArgs footgun 2026-08-04.
                detail = $"{target.Detail}+{bodyDetail}";
            }
            else
            {
                var lineSpan = target.Node.GetLocation().GetLineSpan();
                range = new BracketSyntaxResolve.TextRange(
                    lineSpan.StartLinePosition.Line + 1,
                    lineSpan.StartLinePosition.Character + 1,
                    lineSpan.EndLinePosition.Line + 1,
                    Math.Max(1, lineSpan.EndLinePosition.Character + 1));
                detail = target.Detail;
            }

            // SoftFL (lived UnbindLifecycle wipe 2026-08-07): old_string= with M: must patch
            // inside the locus — never ignore old_string while place=replace eats the member.
            var oldInLocus = OptString(args, "old_string");
            if (oldInLocus is { Length: > 0 } && place is "replace")
            {
                store.ApplyReplaceInRange(
                    buf,
                    range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd,
                    oldInLocus, replacement);
                return new
                {
                    family = "csharp",
                    wire = BracketLocate.Format(span),
                    resolve = detail + "+old_string",
                    place = "in_locus",
                    range = new
                    {
                        start_line = range.LineStart,
                        start_column = range.ColumnStart,
                        end_line = range.LineEnd,
                        end_column = range.ColumnEnd
                    }
                };
            }

            RefuseLargeMemberReplaceWipe(args, place, span, range, replacement, buf.Text);

            var applyPlace = place is "into" ? "before" : place is "end" ? "after" : place;
            ApplyPlacedRange(
                store, buf, applyPlace,
                range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd,
                replacement);

            return new
            {
                family = "csharp",
                wire = BracketLocate.Format(span),
                resolve = detail,
                place,
                range = new
                {
                    start_line = range.LineStart,
                    start_column = range.ColumnStart,
                    end_line = range.LineEnd,
                    end_column = range.ColumnEnd
                }
            };
        }

        // Xml family
        if (!string.Equals(buf.Language, "xml", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"axes_mismatch: xml axes on language={buf.Language}. path={buf.Path}");

        if (!BracketXmlResolve.TryResolve(buf.Path, buf.Text, span, out var xml, out var xmlDetail))
            throw new ArgumentException($"Anchor resolve failed ({xmlDetail}): {wire}");

        var textToWrite = replacement;
        if (xml.Insert)
        {
            // +K:Element insert is its own place semantics — do not also honor place=before/after
            // as a second axis (would double-apply). place= must be omit/replace.
            if (place is not "replace")
                throw new ArgumentException(
                    "xml +K:Element insert already places the node; omit place= or use place=replace.");
            if (string.IsNullOrWhiteSpace(xml.InsertElementName))
                throw new ArgumentException("xml_insert_missing_element_name");
            textToWrite = BracketXmlResolve.BuildInsertElement(
                xml.InsertElementName,
                replacement,
                xml.InsertIndent ?? "  ");
        }

        ApplyPlacedRange(
            store, buf, place,
            xml.Range.LineStart, xml.Range.ColumnStart, xml.Range.LineEnd, xml.Range.ColumnEnd,
            textToWrite);

        return new
        {
            family = "xml",
            wire = BracketLocate.Format(span),
            resolve = xml.Detail,
            insert = xml.Insert,
            place,
            range = new
            {
                start_line = xml.Range.LineStart,
                start_column = xml.Range.ColumnStart,
                end_line = xml.Range.LineEnd,
                end_column = xml.Range.ColumnEnd
            }
        };
    }

    /// <summary>
    /// <c>place=</c> for <c>edit_op=anchor</c>:
    /// <list type="bullet">
    /// <item><c>before|after</c> — insert at locus edges (method = sibling outside; type/ns = inside braces).</item>
    /// <item><c>into|end</c> — always insert inside method/type braces (body start / before close).</item>
    /// <item><c>replace</c> (default) — overwrite locus.</item>
    /// </list>
    /// Silent ignore was ultra-critical — agents passed place=before and wiped the member.
    /// </summary>
    static string NormalizeAnchorPlace(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "replace";
        var p = raw.Trim().ToLowerInvariant();
        return p switch
        {
            "before" or "pre" or "b" => "before",
            "after" or "post" or "a" => "after",
            "into" or "body" or "start" or "in" => "into",
            "end" or "into_end" or "body_end" => "end",
            "replace" or "over" or "r" or "into_replace" => "replace",
            "sniper" or "hold" or "target" => throw new ArgumentException(
                "place=sniper is paste/put only — for anchor use place=before|after|into|end|replace."),
            _ => throw new ArgumentException(
                $"Unknown place='{raw}' for edit_op=anchor — use before|after|into|end|replace.")
        };
    }
    /// <summary>
    /// SoftFL ADX-HX-002: bare <c>place=replace</c> on a large M: locus without <c>T:</c>/<c>old_string=</c>
    /// silently wipes the member when agents pass a tiny patch body (lived UnbindLifecycle).
    /// </summary>
    static void RefuseLargeMemberReplaceWipe(
        IReadOnlyDictionary<string, JsonElement> args,
        string place,
        BracketLocate.Span span,
        BracketSyntaxResolve.TextRange range,
        string replacement,
        string bufferText)
    {
        _ = bufferText;
        if (place is not "replace")
            return;
        if (!string.IsNullOrWhiteSpace(span.TextNeedle))
            return;
        if (BoolOr(args, "force", defaultValue: false))
            return;

        var locusLines = range.LineEnd - range.LineStart + 1;
        if (locusLines < 6)
            return;

        var bodyLines = CountBodyLines(replacement);
        // Tiny patch vs multi-line member = crook (2 lines into ~70-line UnbindLifecycle).
        if (bodyLines * 2 >= locusLines)
            return;

        throw new InvalidOperationException(
            $"Refusing place=replace on large M: locus ({locusLines} lines → {bodyLines} lines) without old_string= (ADX-HX-002). " +
            "This overwrites the whole member — lived UnbindLifecycle wipe. " +
            "Pass old_string= for in-locus patch, T: needle, or force=true for intentional full rewrite.");
    }

    static int CountBodyLines(string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;
        var n = 1;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\n')
                n++;
        }

        // Trailing newline does not add an empty visual line for this guard.
        if (s.EndsWith('\n'))
            n--;
        return Math.Max(0, n);
    }


    /// <summary>
    /// Body-interior places, or type/namespace where before|after mean "first/last member inside".
    /// Method/ctor/… + before|after stay sibling-outside (agent "new helper before this method").
    /// </summary>
    static bool WantsBlockInteriorPlace(string place, Microsoft.CodeAnalysis.SyntaxNode node)
    {
        if (place is "into" or "end")
            return true;
        if (place is not ("before" or "after"))
            return false;

        return node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax
            or Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax
            or Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax;
    }

    static void ApplyPlacedRange(
        DocumentBufferStore store,
        DocBuffer buf,
        string place,
        int lineStart,
        int colStart,
        int lineEnd,
        int colEnd,
        string text)
    {
        if (place == "replace")
        {
            store.ApplyReplaceRange(buf, lineStart, colStart, lineEnd, colEnd, text);
            return;
        }

        if (place == "before")
        {
            // Zero-width insert point (block interior / T: edge): keep column.
            // Multi-line locus (sibling before member): col 1 so leading indent stays on the locus.
            var col = lineStart == lineEnd && colStart == colEnd ? colStart : 1;
            store.ApplyReplaceRange(buf, lineStart, col, lineStart, col, text);
            return;
        }

        // after — exclusive end point of resolved locus (also zero-width when body-narrowed)
        store.ApplyReplaceRange(buf, lineEnd, colEnd, lineEnd, colEnd, text);
    }

    /// <summary>
    /// Relative <c>path=</c> → <see cref="SessionContext.ProjectRoot"/> (else process cwd).
    /// Absolute unchanged. Aligns buffer plane with anchor <c>F:</c> resolve — not MCP host home.
    /// </summary>
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

    static string ResolveAnchorFilePath(
        DocBuffer buf,
        SessionContext session,
        BracketLocate.Span span,
        string? pathArg)
    {
        if (pathArg is { Length: > 0 })
            return ResolveUserPath(session, pathArg);

        if (!string.IsNullOrWhiteSpace(span.File))
        {
            var f = span.File.Trim();
            if (Path.IsPathRooted(f))
                return Path.GetFullPath(f);

            var root = session.ProjectRoot
                ?? Path.GetDirectoryName(buf.Path)
                ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(root, f));
        }

        return buf.Path;
    }

    static string ResolvePathKey(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        if (path is { Length: > 0 })
            return ResolveUserPath(session, path);

        var wire = OptString(args, "anchor") ?? OptString(args, "at");
        if (wire is { Length: > 0 })
        {
            var span = BracketLocate.Parse(wire);
            if (!string.IsNullOrWhiteSpace(span.File))
            {
                var f = span.File.Trim();
                if (Path.IsPathRooted(f))
                    return Path.GetFullPath(f);
                var root = session.ProjectRoot ?? Directory.GetCurrentDirectory();
                return Path.GetFullPath(Path.Combine(root, f));
            }
        }

        if (OptString(args, "doc_id") is { Length: > 0 })
            return store.Resolve(null, OptString(args, "doc_id")).Path;

        throw new ArgumentException(
            "edit needs path=, doc_id=, or anchor with F: file (project-relative or absolute).");
    }

}
