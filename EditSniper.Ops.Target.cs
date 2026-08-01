using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>Target outline inside corridor (≤ADX soft-warn peel).</summary>
internal static partial class EditSniper
{
    static string Target(
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
                op = "target",
                error = "no_scope",
                hint = "go=scope from=… first"
            }, Pretty);
        }

        var h = Hold;
        var text = ReadText(store, h.Path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var max = Math.Clamp(IntOr(args, "max", MaxTargets), 1, 120);

        var nodes = new List<object>();
        foreach (var node in root.DescendantNodes())
        {
            if (!IsInteresting(node))
                continue;
            var span = node.GetLocation().GetLineSpan();
            var ls = span.StartLinePosition.Line + 1;
            var le = span.EndLinePosition.Line + 1;
            if (!Overlaps(ls, le, h.LineStart, h.LineEnd))
                continue;
            if (ls < h.LineStart)
                continue;

            var member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            var memberKey = member is null ? null : MemberName(member);
            var kind = KindOf(node);
            var name = NameOf(node);
            var wire = BracketLocate.Format(new BracketLocate.Span(
                h.FileLabel,
                memberKey,
                ls,
                le == ls ? null : le));

            nodes.Add(new
            {
                kind,
                name,
                member = memberKey,
                line_start = ls,
                line_end = le,
                wire
            });

            if (nodes.Count >= max)
                break;
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "target",
            hold = HoldCard(),
            count = nodes.Count,
            truncated = nodes.Count >= max,
            targets = nodes,
            next = new object[]
            {
                new { go = "peek", label = "Peek wire/corridor", why = "go_args.wire= optional" },
                new { go = "edit_draft", label = "Draft mutate/fix", why = "pick wire → YAML / fix" },
                new { go = "scope_clear", label = "Clear aim", why = "drop corridor" }
            },
            hint =
                "Targets are in-corridor only. go=peek wire=… → tiny window; then edit_plan. " +
                "Wide file outline is not default."
        }, Pretty);
    }
}
