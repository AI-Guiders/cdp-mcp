using System.Text.Json;
using Cdp.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>Query parse + score + walk helpers for Go To All (≤ADX soft-warn peel).</summary>
internal static partial class GoToAll
{
    static (string Kind, string Query) ParseQuery(string raw, string? kindArg)
    {
        var kind = (kindArg ?? "all").Trim().ToLowerInvariant();
        kind = kind switch
        {
            "f" or "files" => "file",
            "t" or "types" or "class" or "classes" => "type",
            "m" or "members" or "method" or "methods" => "member",
            "s" or "symbols" or "#" => "symbol",
            "q" or "cmd" or "command" or "commands" or "feat" or "features" => "feature",
            _ => kind
        };

        // VS-style: "t Foo", "f:Bar", "#Baz", "q undo"
        if (raw.Length >= 2)
        {
            var c0 = char.ToLowerInvariant(raw[0]);
            var sep = raw[1];
            if ((sep is ' ' or ':' or '/') && c0 is 'f' or 't' or 'm' or '#' or 's' or 'q')
            {
                kind = c0 switch
                {
                    'f' => "file",
                    't' => "type",
                    'm' => "member",
                    '#' or 's' => "symbol",
                    'q' => "feature",
                    _ => kind
                };
                return (kind, raw[2..].Trim());
            }
        }

        return (kind is "file" or "type" or "member" or "symbol" or "feature" or "all" ? kind : "all", raw);
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
