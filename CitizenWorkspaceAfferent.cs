#nullable enable

namespace CdpMcp;

/// <summary>
/// Face SoftFL lantern — session + open-buffer Afferent lines (entry + active workspace).
/// SoftInstrument invent REJECT — densify Completions inject, not a new organ.
/// </summary>
internal static class CitizenWorkspaceAfferent
{
    const int MaxOpenNames = 3;

    /// <summary>Pure format for tests.</summary>
    public static string FormatSession(
        string? projectRoot,
        string? language,
        string? solutionOrProjectPath)
    {
        var leaf = LeafName(projectRoot);
        if (leaf is null && string.IsNullOrWhiteSpace(solutionOrProjectPath))
            return "session | root=? · dig=@intent project_root|onboard|domain";

        var lang = string.IsNullOrWhiteSpace(language) ? "?" : language.Trim();
        var proj = string.IsNullOrWhiteSpace(solutionOrProjectPath)
            ? "?"
            : Path.GetFileName(solutionOrProjectPath.Trim());
        return $"session | root={leaf ?? "?"} · {lang} · proj={proj} · dig=@intent project_root|domain card=id=citizen|onboard";
    }

    /// <summary>Pure format for tests.</summary>
    public static string FormatEditor(int count, IReadOnlyList<string> fileNames, string? focusName)
    {
        if (count <= 0)
            return "editor | 0 buf · dig=@intent editor_scene|buffer open path=";

        var names = fileNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Take(MaxOpenNames)
            .ToArray();
        var open = names.Length == 0 ? "—" : string.Join(", ", names);
        var focus = string.IsNullOrWhiteSpace(focusName) ? names.FirstOrDefault() ?? "—" : focusName.Trim();
        var more = count > names.Length ? $" · +{count - names.Length}" : "";
        return $"editor | {count} buf · focus={focus} · open={open}{more} · dig=@intent editor_scene";
    }

    /// <summary>Short pulse for F seat board line.</summary>
    public static string? EditorSeatPulse()
    {
        try
        {
            var store = IdeLanguageTools.TryGetDocumentStore();
            if (store is null)
                return null;
            var docs = store.All.OrderBy(b => b.DocId, StringComparer.Ordinal).ToArray();
            if (docs.Length == 0)
                return null; // keep board pin «editor» when empty
            var focus = FocusFileName(docs) ?? Path.GetFileName(docs[0].Path);
            var dirty = docs.Count(d => d.Dirty);
            return dirty > 0
                ? $"{docs.Length} buf · dirty×{dirty} · {focus}"
                : $"{docs.Length} buf · {focus}";
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> TryCaptureLines()
    {
        try
        {
            var root = IdePressureChannel.TryPeekProjectRoot();
            var store = IdeLanguageTools.TryGetDocumentStore();
            var docs = store?.All.OrderBy(b => b.DocId, StringComparer.Ordinal).ToArray() ?? [];

            if (string.IsNullOrWhiteSpace(root) && docs.Length > 0)
                root = InferRootFromBuffers(docs);

            var lang = InferLanguage(docs, root);
            var proj = InferProjectFile(root);
            var names = docs.Select(d => Path.GetFileName(d.Path)).Where(n => n.Length > 0).ToArray();
            var focus = FocusFileName(docs);

            return
            [
                FormatSession(root, lang, proj),
                FormatEditor(docs.Length, names, focus)
            ];
        }
        catch
        {
            return
            [
                FormatSession(null, null, null),
                FormatEditor(0, [], null)
            ];
        }
    }

    static string? LeafName(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return null;
        try
        {
            return new DirectoryInfo(projectRoot.Trim()).Name;
        }
        catch
        {
            return Path.GetFileName(projectRoot.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    static string? InferRootFromBuffers(IReadOnlyList<DocBuffer> docs)
    {
        foreach (var d in docs)
        {
            var dir = Path.GetDirectoryName(d.Path);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.EnumerateFiles(dir, "*.csproj").Any()
                    || Directory.EnumerateFiles(dir, "*.sln").Any())
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }

        return Path.GetDirectoryName(docs[0].Path);
    }

    static string InferLanguage(IReadOnlyList<DocBuffer> docs, string? root)
    {
        if (docs.Any(d => d.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            return "csharp";
        if (docs.Any(d => d.Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || d.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)))
            return "typescript";
        if (!string.IsNullOrWhiteSpace(root)
            && Directory.Exists(root)
            && Directory.EnumerateFiles(root, "*.csproj").Any())
            return "csharp";
        return "?";
    }

    static string? InferProjectFile(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return null;
        try
        {
            var leaf = LeafName(root);
            if (leaf is not null)
            {
                var named = Path.Combine(root, leaf + ".csproj");
                if (File.Exists(named))
                    return named;
            }

            var first = Directory.EnumerateFiles(root, "*.csproj").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            return first ?? Directory.EnumerateFiles(root, "*.sln").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    static string? FocusFileName(IReadOnlyList<DocBuffer> docs)
    {
        var nav = EditorComfort.TryPeekNavCurrent();
        if (!string.IsNullOrWhiteSpace(nav))
        {
            // wire like [F:GlassIntercomMention.cs]
            var start = nav.IndexOf(':');
            var end = nav.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                var inner = nav[(start + 1)..end].Trim();
                if (inner.Length > 0)
                    return Path.GetFileName(inner);
            }

            return Path.GetFileName(nav.Trim('[', ']'));
        }

        return docs.Count > 0 ? Path.GetFileName(docs[0].Path) : null;
    }
}
