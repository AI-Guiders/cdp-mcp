using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>File/symbol hit collection for Go To All.</summary>
internal static partial class GoToAll
{
    static void AddFiles(List<Hit> hits, SessionContext session, string root, string query, int max)
    {
        var n = 0;
        foreach (var path in EnumerateCs(root))
        {
            if (n++ > MaxFilesScan || hits.Count >= max * 3)
                break;
            var name = Path.GetFileName(path);
            var score = Score(name, query);
            if (score <= 0)
            {
                score = Score(Path.GetFileNameWithoutExtension(name), query);
                if (score <= 0)
                    continue;
                score -= 5;
            }

            hits.Add(new Hit(
                "file",
                name,
                score,
                BracketLocate.Format(new BracketLocate.Span(FileLabel(session, path), null, null, null))));
        }
    }

    static void AddSymbols(
        List<Hit> hits,
        DocumentBufferStore store,
        SessionContext session,
        string root,
        string query,
        string kind,
        int max)
    {
        var n = 0;
        foreach (var path in EnumerateCs(root))
        {
            if (n++ > MaxFilesScan || hits.Count >= max * 4)
                break;

            string text;
            try
            {
                var open = store.All.FirstOrDefault(b =>
                    string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase));
                text = open?.Text ?? File.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            var label = FileLabel(session, path);
            var rootNode = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();

            if (kind is "all" or "type" or "symbol")
            {
                foreach (var type in rootNode.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    var name = type.Identifier.ValueText;
                    var score = Score(name, query);
                    if (score <= 0)
                        continue;
                    var line = type.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    hits.Add(new Hit(
                        "type",
                        name,
                        score + 10,
                        BracketLocate.Format(new BracketLocate.Span(label, name, line, null))));
                }
            }

            if (kind is "all" or "member" or "symbol")
            {
                foreach (var member in rootNode.DescendantNodes().OfType<MemberDeclarationSyntax>())
                {
                    var name = MemberName(member);
                    if (name is null)
                        continue;
                    // Skip type decls here — already covered.
                    if (member is BaseTypeDeclarationSyntax)
                        continue;
                    var score = Score(name, query);
                    if (score <= 0)
                        continue;
                    var line = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var container = member.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()
                        ?.Identifier.ValueText;
                    hits.Add(new Hit(
                        "member",
                        container is null ? name : $"{container}.{name}",
                        score,
                        BracketLocate.Format(new BracketLocate.Span(label, name, line, null))));
                }
            }
        }
    }
}
