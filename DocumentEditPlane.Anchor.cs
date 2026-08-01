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

            if (!BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, span, out var range, out var detail))
                throw new ArgumentException($"Anchor resolve failed ({detail}): {wire}");

            ApplyPlacedRange(
                store, buf, place,
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
    /// <c>place=</c> for <c>edit_op=anchor</c>: before|after insert at locus edges; replace (default) overwrites.
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
            "replace" or "over" or "r" or "into" => "replace",
            "sniper" or "hold" or "target" => throw new ArgumentException(
                "place=sniper is paste/put only — for anchor use place=before|after|replace."),
            _ => throw new ArgumentException(
                $"Unknown place='{raw}' for edit_op=anchor — use before|after|replace.")
        };
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
            // Insert at line start (col 1), not member token col — otherwise leading
            // indent of the locus sticks to the inserted text and the old member loses it.
            store.ApplyReplaceRange(buf, lineStart, 1, lineStart, 1, text);
            return;
        }

        // after — exclusive end point of resolved locus
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
