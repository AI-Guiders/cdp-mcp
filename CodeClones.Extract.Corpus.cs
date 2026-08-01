using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdpMcp;

/// <summary>Corpus walk / path helpers for CodeClones extract.</summary>
internal static partial class CodeClones
{
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
