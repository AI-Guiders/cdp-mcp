using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>Roslyn window extract, fingerprint, corpus walk for CodeClones.</summary>
internal static partial class CodeClones
{
    static IEnumerable<Fragment> ExtractWindows(
        string absolutePath,
        string fileLabel,
        string text,
        int minStatements)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();

        foreach (var member in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            var body = member.Body;
            if (body is null)
                continue;

            var stmts = body.Statements;
            if (stmts.Count < minStatements)
                continue;

            var memberName = MemberName(member);
            // Whole-body window (VS solution scan analogue for method-sized clones).
            yield return BuildFragment(
                absolutePath, fileLabel, memberName, stmts, 0, stmts.Count);

            // Sliding windows for large methods.
            if (stmts.Count > minStatements + 2)
            {
                for (var start = 0; start <= stmts.Count - minStatements; start++)
                {
                    var len = Math.Min(stmts.Count - start, Math.Max(minStatements, Math.Min(24, stmts.Count - start)));
                    if (len < minStatements)
                        break;
                    // Skip the full-body window already emitted.
                    if (start == 0 && len == stmts.Count)
                        continue;
                    yield return BuildFragment(
                        absolutePath, fileLabel, memberName, stmts, start, len);
                    // Step by ~half window to limit explosion.
                    start += Math.Max(1, len / 2) - 1;
                }
            }
        }
    }

    static Fragment BuildFragment(
        string absolutePath,
        string fileLabel,
        string? member,
        SyntaxList<StatementSyntax> stmts,
        int start,
        int length)
    {
        var slice = stmts.Skip(start).Take(length).ToArray();
        var lineStart = slice[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var lineEnd = slice[^1].GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        var exact = Fingerprint(slice, normalizeIds: false);
        var strong = Fingerprint(slice, normalizeIds: true);
        return new Fragment(
            absolutePath,
            fileLabel,
            member,
            lineStart,
            lineEnd,
            exact,
            strong,
            slice.Length);
    }

    static string Fingerprint(StatementSyntax[] stmts, bool normalizeIds)
    {
        var sb = new StringBuilder(256);
        foreach (var s in stmts)
        {
            var node = normalizeIds ? Normalize(s) : s.NormalizeWhitespace();
            sb.Append(node.ToFullString().Trim());
            sb.Append('\u001e');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 16));
    }

    static SyntaxNode Normalize(SyntaxNode node)
    {
        var rewritten = new IdNormalizer().Visit(node) ?? node;
        return rewritten.NormalizeWhitespace();
    }

    sealed class IdNormalizer : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            SyntaxFactory.IdentifierName("id").WithTriviaFrom(node);

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression)
                || node.IsKind(SyntaxKind.CharacterLiteralExpression))
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal("")).WithTriviaFrom(node);
            if (node.IsKind(SyntaxKind.NumericLiteralExpression))
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(0)).WithTriviaFrom(node);
            return base.VisitLiteralExpression(node);
        }
    }

    static Fragment? TryBuildSeed(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string? wire,
        int minStatements,
        out string? error)
    {
        error = null;
        string? abs = null;
        string? label = null;
        int? lineStart = null;
        int? lineEnd = null;
        string? memberHint = null;

        if (wire is { Length: > 0 })
        {
            try
            {
                var span = BracketLocate.Parse(wire);
                if (span.File is { Length: > 0 })
                {
                    abs = ResolveUserPath(session, span.File);
                    label = FileLabel(session, abs);
                }

                memberHint = span.MemberKey;
                lineStart = span.LineStart;
                lineEnd = span.LineEnd ?? span.LineStart;
            }
            catch (Exception ex)
            {
                error = $"bad_anchor: {ex.Message}";
                return null;
            }
        }

        abs ??= OptString(args, "path") is { Length: > 0 } p
            ? ResolveUserPath(session, p)
            : store.All.FirstOrDefault()?.Path;

        if (abs is null)
        {
            error = "no_seed_file";
            return null;
        }

        label ??= FileLabel(session, abs);
        lineStart ??= IntOrNullable(args, "start_line");
        lineEnd ??= IntOrNullable(args, "end_line") ?? lineStart;

        string text;
        try
        {
            text = ReadText(store, abs);
        }
        catch (Exception ex)
        {
            error = $"read_failed: {ex.Message}";
            return null;
        }

        var windows = ExtractWindows(abs, label, text, Math.Min(minStatements, DefaultMinStatementsLocal))
            .ToList();

        Fragment? pick = null;
        if (lineStart is int ls)
        {
            var le = lineEnd ?? ls;
            pick = windows
                .Where(w => w.LineStart <= le && w.LineEnd >= ls)
                .OrderBy(w => Math.Abs(w.LineStart - ls) + Math.Abs(w.LineEnd - le))
                .ThenBy(w => w.StatementCount)
                .FirstOrDefault();
        }

        if (pick is null && memberHint is { Length: > 0 })
        {
            pick = windows
                .Where(w => string.Equals(w.Member, memberHint, StringComparison.Ordinal))
                .OrderByDescending(w => w.StatementCount)
                .FirstOrDefault();
        }

        if (pick is null)
        {
            error = "seed_not_found";
            return null;
        }

        return pick;
    }

    static bool TryCollectCorpus(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string scope,
        Fragment? seed,
        int maxFiles,
        out List<(string Abs, string Label)> files,
        out string? error)
    {
        var list = new List<(string Abs, string Label)>();
        files = list;
        error = null;

        void AddFile(string abs)
        {
            if (list.Count >= maxFiles)
                return;
            var full = Path.GetFullPath(abs);
            if (!full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return;
            if (IsExcludedPath(full))
                return;
            if (list.Any(f => string.Equals(f.Abs, full, StringComparison.OrdinalIgnoreCase)))
                return;
            list.Add((full, FileLabel(session, full)));
        }

        switch (scope)
        {
            case "file":
            case "method":
            case "selection":
            {
                var path = OptString(args, "path");
                if (path is { Length: > 0 })
                    AddFile(ResolveUserPath(session, path));
                else if (seed is not null)
                    AddFile(seed.AbsolutePath);
                else if (store.All.FirstOrDefault() is { } buf)
                    AddFile(buf.Path);
                else
                {
                    error = "no_file";
                    return false;
                }

                // Seed (selection/method): default search_in=project when open (VS Find Matching Clones).
                var searchIn = (OptString(args, "search_in") ?? OptString(args, "search_scope") ?? "").Trim()
                    .ToLowerInvariant();
                if (string.IsNullOrEmpty(searchIn))
                {
                    searchIn = seed is not null && session.ProjectRoot is { Length: > 0 }
                        ? "project"
                        : "file";
                }

                if (seed is not null && searchIn is "project" or "solution")
                    AddTree(session.ProjectRoot, maxFiles, AddFile);

                return list.Count > 0;
            }
            case "project":
            case "solution":
            {
                var root = session.ProjectRoot;
                if (string.IsNullOrWhiteSpace(root))
                {
                    error = "no_project";
                    return false;
                }

                AddTree(root, maxFiles, AddFile);
                if (list.Count == 0)
                {
                    error = "no_cs_files";
                    return false;
                }

                return true;
            }
            default:
                error = "unknown_scope";
                return false;
        }
    }

    static void AddTree(string? root, int maxFiles, Action<string> add)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(file))
                continue;
            add(file);
            // Approximate stop — AddFile also caps.
            if (maxFiles <= 0)
                break;
        }
    }

    static bool IsExcludedPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => SkipDirNames.Contains(p)))
            return true;
        var name = Path.GetFileName(path);
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    static string WireOf(Fragment f) =>
        BracketLocate.Format(new BracketLocate.Span(
            f.FileLabel,
            f.Member,
            f.LineStart,
            f.LineEnd == f.LineStart ? null : f.LineEnd));

    static string? MemberName(BaseMethodDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.ValueText,
        ConstructorDeclarationSyntax x => x.Identifier.ValueText,
        DestructorDeclarationSyntax x => x.Identifier.ValueText,
        OperatorDeclarationSyntax => "operator",
        ConversionOperatorDeclarationSyntax => "conversion",
        _ => null
    };

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
            throw new FileNotFoundException("clone path missing", path);
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
}
