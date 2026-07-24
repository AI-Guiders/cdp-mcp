using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>
/// VS Ctrl+T / Go To All — land on anchors (file / type / member), not text Find.
/// Lean: project walk + Roslyn syntax names. Prefixes: f t m # (VS-style).
/// </summary>
internal static class GoToAll
{
    public const string Schema = "goto_all/v0";
    public const string ToolName = "cdp_goto";
    public const int DefaultMax = 40;
    public const int MaxFilesScan = 400;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults", "publish", "publish-release", "dist"
    };

    public static bool IsGoToTool(string name) =>
        string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var raw = (Opt(args, "query") ?? Opt(args, "q") ?? Opt(args, "text") ?? "").Trim();
        if (raw.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "query_required",
                hint = "query= Foo | f Foo | t Bar | m Baz  (VS Ctrl+T prefixes)"
            }, Pretty);
        }

        var (kind, query) = ParseQuery(raw, Opt(args, "kind") ?? Opt(args, "filter"));
        if (query.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "empty_query_after_prefix"
            }, Pretty);
        }

        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "no_project",
                hint = "cdp_open first"
            }, Pretty);
        }

        var max = Math.Clamp(IntOr(args, "max", DefaultMax), 1, 100);
        var hits = new List<Hit>();

        if (kind is "all" or "file")
            AddFiles(hits, session, root!, query, max);

        if (kind is "all" or "type" or "member" or "symbol")
            AddSymbols(hits, store, session, root!, query, kind, max);

        var ranked = hits
            .GroupBy(h => h.Anchor, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(h => h.Score).First())
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Kind, StringComparer.Ordinal)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();

        if (ranked.Length > 0)
            EditorComfort.PushLocus(session, ranked[0].Anchor);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            query,
            kind,
            count = ranked.Length,
            hits = ranked.Select(h => new
            {
                kind = h.Kind,
                name = h.Name,
                score = h.Score,
                anchor = h.Anchor
            }),
            next = ranked.Length > 0
                ? (object[])
                [
                    new { go = "peek", label = "Peek top", why = $"wire={ranked[0].Anchor}" },
                    new { go = "scope", label = "Sniper from hit", why = "from= top anchor" }
                ]
                : (object[])
                [
                    new { go = "goto", label = "Retry", why = "f:|t:|m: prefix or broader query" }
                ],
            hint = "VS Ctrl+T analogue. Results are anchors. Prefixed: f file, t type, m member, # symbol."
        }, Pretty);
    }

    static (string Kind, string Query) ParseQuery(string raw, string? kindArg)
    {
        var kind = (kindArg ?? "all").Trim().ToLowerInvariant();
        kind = kind switch
        {
            "f" or "files" => "file",
            "t" or "types" or "class" or "classes" => "type",
            "m" or "members" or "method" or "methods" => "member",
            "s" or "symbols" or "#" => "symbol",
            _ => kind
        };

        // VS-style: "t Foo", "f:Bar", "#Baz"
        if (raw.Length >= 2)
        {
            var c0 = char.ToLowerInvariant(raw[0]);
            var sep = raw[1];
            if ((sep is ' ' or ':' or '/') && c0 is 'f' or 't' or 'm' or '#' or 's')
            {
                kind = c0 switch
                {
                    'f' => "file",
                    't' => "type",
                    'm' => "member",
                    '#' or 's' => "symbol",
                    _ => kind
                };
                return (kind, raw[2..].Trim());
            }
        }

        return (kind is "file" or "type" or "member" or "symbol" or "all" ? kind : "all", raw);
    }

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

    static string? MemberName(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.ValueText,
        PropertyDeclarationSyntax x => x.Identifier.ValueText,
        ConstructorDeclarationSyntax x => x.Identifier.ValueText,
        FieldDeclarationSyntax x => x.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
        EventDeclarationSyntax x => x.Identifier.ValueText,
        IndexerDeclarationSyntax => "this",
        _ => null
    };

    /// <summary>Higher is better. 0 = no match.</summary>
    static int Score(string name, string query)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(query))
            return 0;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 1000;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 800 - Math.Min(200, name.Length - query.Length);
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 500 - Math.Min(200, name.IndexOf(query, StringComparison.OrdinalIgnoreCase));
        if (CamelMatch(name, query))
            return 300;
        return 0;
    }

    static bool CamelMatch(string name, string query)
    {
        // "gta" → GoToAll (initials + embedded capitals)
        var acro = string.Concat(name.Where((c, i) => i == 0 || char.IsUpper(c)));
        if (acro.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return true;
        var qi = 0;
        foreach (var ch in acro)
        {
            if (qi < query.Length && char.ToLowerInvariant(ch) == char.ToLowerInvariant(query[qi]))
                qi++;
        }

        return qi == query.Length && query.Length >= 2;
    }

    static IEnumerable<string> EnumerateCs(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsSkipped(file))
                continue;
            yield return file;
        }
    }

    static bool IsSkipped(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => SkipDirs.Contains(p)))
            return true;
        var name = Path.GetFileName(path);
        return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);
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
                return full[(fullRoot.Length + 1)..].Replace('\\', '/');
        }

        return Path.GetFileName(absolutePath);
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int d)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number)
            return d;
        return el.TryGetInt32(out var n) ? n : d;
    }

    sealed record Hit(string Kind, string Name, int Score, string Anchor);
}
