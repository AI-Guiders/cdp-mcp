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
    static bool TryExpandToMemberBody(
        DocumentBufferStore store,
        string path,
        BracketSyntaxResolve.TextRange from,
        out BracketSyntaxResolve.TextRange zone,
        out string detail,
        out string error)
    {
        zone = from;
        detail = "";
        error = "";
        var text = ReadText(store, path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var pos = tree.GetText().Lines[Math.Clamp(from.LineStart - 1, 0, tree.GetText().Lines.Count - 1)].Start
                  + Math.Max(0, from.ColumnStart - 1);
        var node = root.FindToken(pos).Parent;
        BlockSyntax? body =
            node?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Body
            ?? node?.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().FirstOrDefault()?.Body
            ?? node?.AncestorsAndSelf().OfType<AccessorDeclarationSyntax>().FirstOrDefault()?.Body
            ?? node?.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault()?.Body;
        if (body is null)
        {
            error = "member_body_not_found";
            return false;
        }

        var span = body.GetLocation().GetLineSpan();
        zone = new BracketSyntaxResolve.TextRange(
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            Math.Max(1, span.EndLinePosition.Character + 1));
        detail = "member_body";
        return true;
    }

    static bool TryExpandToEnclosingMember(
        DocumentBufferStore store,
        string path,
        BracketSyntaxResolve.TextRange from,
        out BracketSyntaxResolve.TextRange zone,
        out string detail,
        out string error)
    {
        zone = from;
        detail = "";
        error = "";
        var text = ReadText(store, path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var pos = tree.GetText().Lines[Math.Clamp(from.LineStart - 1, 0, tree.GetText().Lines.Count - 1)].Start
                  + Math.Max(0, from.ColumnStart - 1);
        var node = root.FindToken(pos).Parent;
        var member = node?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (member is null)
        {
            error = "enclosing_member_not_found";
            return false;
        }

        var span = member.GetLocation().GetLineSpan();
        zone = new BracketSyntaxResolve.TextRange(
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            Math.Max(1, span.EndLinePosition.Character + 1));
        detail = "enclosing_member";
        return true;
    }

    static BracketSyntaxResolve.TextRange MergeZones(
        BracketSyntaxResolve.TextRange a,
        BracketSyntaxResolve.TextRange b)
    {
        var lineStart = Math.Min(a.LineStart, b.LineStart);
        var lineEnd = Math.Max(a.LineEnd, b.LineEnd);
        var colStart = a.LineStart <= b.LineStart ? a.ColumnStart : b.ColumnStart;
        var colEnd = a.LineEnd >= b.LineEnd ? a.ColumnEnd : b.ColumnEnd;
        return new BracketSyntaxResolve.TextRange(lineStart, colStart, lineEnd, colEnd);
    }

    static bool IsInteresting(SyntaxNode node) => node is
        MethodDeclarationSyntax
        or PropertyDeclarationSyntax
        or ConstructorDeclarationSyntax
        or LocalFunctionStatementSyntax
        or LocalDeclarationStatementSyntax
        or IfStatementSyntax
        or WhileStatementSyntax
        or ForStatementSyntax
        or ForEachStatementSyntax
        or SwitchStatementSyntax
        or ReturnStatementSyntax
        or ThrowStatementSyntax
        or TryStatementSyntax
        or LockStatementSyntax
        or UsingStatementSyntax;

    static string KindOf(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax => "method",
        PropertyDeclarationSyntax => "property",
        ConstructorDeclarationSyntax => "ctor",
        LocalFunctionStatementSyntax => "local_function",
        LocalDeclarationStatementSyntax => "local",
        IfStatementSyntax => "if",
        WhileStatementSyntax => "while",
        ForStatementSyntax => "for",
        ForEachStatementSyntax => "foreach",
        SwitchStatementSyntax => "switch",
        ReturnStatementSyntax => "return",
        ThrowStatementSyntax => "throw",
        TryStatementSyntax => "try",
        LockStatementSyntax => "lock",
        UsingStatementSyntax => "using",
        _ => node.Kind().ToString()
    };

    static string? NameOf(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Identifier.ValueText,
        PropertyDeclarationSyntax p => p.Identifier.ValueText,
        ConstructorDeclarationSyntax c => c.Identifier.ValueText,
        LocalFunctionStatementSyntax lf => lf.Identifier.ValueText,
        LocalDeclarationStatementSyntax ld => string.Join(',',
            ld.Declaration.Variables.Select(v => v.Identifier.ValueText)),
        ForEachStatementSyntax fe => fe.Identifier.ValueText,
        _ => null
    };

    static string MemberName(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.ValueText,
        PropertyDeclarationSyntax x => x.Identifier.ValueText,
        ConstructorDeclarationSyntax x => x.Identifier.ValueText,
        FieldDeclarationSyntax x => x.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "field",
        EventDeclarationSyntax x => x.Identifier.ValueText,
        IndexerDeclarationSyntax => "this",
        OperatorDeclarationSyntax => "operator",
        ConversionOperatorDeclarationSyntax => "conversion",
        TypeDeclarationSyntax x => x.Identifier.ValueText,
        _ => m.GetType().Name
    };

    static bool Overlaps(int aStart, int aEnd, int bStart, int bEnd) =>
        aStart <= bEnd && bStart <= aEnd;

    static bool IsBodyTill(string raw) =>
        raw.Equals("body", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("member_body", StringComparison.OrdinalIgnoreCase);

    static bool IsEnclosingTill(string raw) =>
        raw.Equals("enclosing", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("member", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("enclosing_member", StringComparison.OrdinalIgnoreCase);

    static string NormalizeWire(string wire)
    {
        var t = wire.Trim();
        if (!t.StartsWith('['))
            t = "[" + t;
        if (!t.EndsWith(']'))
            t += "]";
        return t;
    }

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

    static string ReadText(DocumentBufferStore store, string path)
    {
        var open = store.All.FirstOrDefault(b =>
            string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase));
        if (open is not null)
            return open.Text;
        if (!File.Exists(path))
            throw new FileNotFoundException("sniper path missing", path);
        return File.ReadAllText(path);
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

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number)
            return defaultValue;
        return el.TryGetInt32(out var n) ? n : defaultValue;
    }
}
