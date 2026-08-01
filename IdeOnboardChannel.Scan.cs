#nullable enable
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Onboard project scan + entrypoint scoring.</summary>
internal static partial class IdeOnboardChannel
{
    static ScanDoc BuildScan(string root)
    {
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var doc = new ScanDoc
        {
            Title = $"onboard · {name}",
            ProjectName = name,
            Root = root,
            ProfileHint = DetectProfileHint(root),
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        CollectDocs(root, doc.Docs);

        foreach (var sln in Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly))
            doc.Solutions.Add(Rel(root, sln));

        var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var verticalCounts = new Dictionary<string, (int Count, string? Sample)>(StringComparer.OrdinalIgnoreCase);
        var entrypoints = new List<Hit>();
        var filesScanned = 0;
        var truncated = false;
        const int cap = 12_000;

        foreach (var full in EnumerateSourceFiles(root))
        {
            filesScanned++;
            if (filesScanned > cap)
            {
                truncated = true;
                break;
            }

            var rel = Rel(root, full);
            var ext = Path.GetExtension(full);
            if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                doc.CsprojCount++;
                continue;
            }

            if (!ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var top = TopSegment(rel);
            if (top is { Length: > 0 })
            {
                folderCounts[top] = folderCounts.GetValueOrDefault(top) + 1;
                if (!verticalCounts.TryGetValue(top, out var v))
                    verticalCounts[top] = (1, rel);
                else
                    verticalCounts[top] = (v.Count + 1, v.Sample ?? rel);
            }

            var fileName = Path.GetFileName(full);
            if (LooksLikeEntrypoint(fileName))
            {
                var stem = Path.GetFileNameWithoutExtension(full);
                entrypoints.Add(new Hit
                {
                    Kind = "entrypoint",
                    Label = stem,
                    Path = rel,
                    Anchor = BracketLocate.Format(new BracketLocate.Span(rel, stem, null, null)),
                    Score = ScoreEntrypoint(fileName)
                });
            }
        }

        doc.FilesScanned = filesScanned;
        doc.Truncated = truncated;
        doc.Entrypoints = entrypoints
            .OrderByDescending(e => e.Score)
            .ThenBy(e => Depth(e.Path))
            .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        doc.TopFolders = folderCounts
            .OrderByDescending(kv => kv.Value)
            .Take(8)
            .Select(kv => new FolderHit { Path = kv.Key, FileCount = kv.Value })
            .ToList();

        doc.Verticals = verticalCounts
            .Where(kv => !IsNoiseVertical(kv.Key))
            .OrderByDescending(kv => kv.Value.Count)
            .Take(8)
            .Select(kv =>
            {
                var sample = kv.Value.Sample;
                string? anchor = null;
                if (sample is { Length: > 0 })
                {
                    var stem = Path.GetFileNameWithoutExtension(sample);
                    anchor = BracketLocate.Format(new BracketLocate.Span(sample, stem, null, null));
                }

                return new VerticalHit
                {
                    Name = kv.Key,
                    FileCount = kv.Value.Count,
                    SamplePath = sample,
                    SampleAnchor = anchor
                };
            })
            .ToList();

        return doc;
    }

    static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }

            foreach (var sub in subDirs)
            {
                var leaf = Path.GetFileName(sub);
                if (SkipDirs.Contains(leaf) || leaf.StartsWith(".", StringComparison.Ordinal))
                    continue;
                stack.Push(sub);
            }

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (var f in files)
            {
                var ext = Path.GetExtension(f);
                if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                    yield return f;
            }
        }
    }

    static void CollectDocs(string root, DocsHint docs)
    {
        foreach (var candidate in new[] { "README.md", "Readme.md", "readme.md", "README.MD" })
        {
            var p = Path.Combine(root, candidate);
            if (!File.Exists(p)) continue;
            docs.HasReadme = true;
            docs.ReadmePath = Rel(root, p);
            break;
        }

        docs.HasDocsDir =
            Directory.Exists(Path.Combine(root, "docs")) ||
            Directory.Exists(Path.Combine(root, "Docs"));

        var adr = 0;
        foreach (var dirName in new[] { "docs", "Docs", "adr", "ADR" })
        {
            var dir = Path.Combine(root, dirName);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(f);
                    if (name.Contains("adr", StringComparison.OrdinalIgnoreCase) ||
                        dir.Contains("adr", StringComparison.OrdinalIgnoreCase))
                    {
                        adr++;
                        if (adr >= 50) break;
                    }
                }
            }
            catch { /* ignore */ }
        }

        docs.AdrCount = adr;
    }

    static string DetectProfileHint(string root)
    {
        var cockpit = Path.Combine(root, "Cockpit");
        if (Directory.Exists(Path.Combine(cockpit, "Channels")) &&
            Directory.Exists(Path.Combine(cockpit, "Composition")) &&
            (Directory.Exists(Path.Combine(root, "IdeDisplay")) || Directory.Exists(Path.Combine(cockpit, "Cds"))))
            return "cide";

        if (File.Exists(Path.Combine(root, "IdeCockpit.cs")) &&
            File.Exists(Path.Combine(root, "IdeCockpit.Build.cs")))
            return "cdp_desk";

        return "unknown";
    }

    static bool LooksLikeEntrypoint(string fileName)
    {
        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.Equals("App.axaml.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("App.xaml.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.Contains("Hosting", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase))
            return false;
        return EntrypointName.IsMatch(fileName);
    }

    static int ScoreEntrypoint(string fileName)
    {
        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) return 100;
        if (fileName.StartsWith("App.", StringComparison.OrdinalIgnoreCase)) return 90;
        if (fileName.Contains("CompositionRoot", StringComparison.OrdinalIgnoreCase)) return 85;
        if (fileName.Contains("Startup", StringComparison.OrdinalIgnoreCase)) return 80;
        if (fileName.Contains("Host", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Contains("Hosting", StringComparison.OrdinalIgnoreCase)) return 70;
        if (fileName.Contains("MainWindow", StringComparison.OrdinalIgnoreCase)) return 60;
        if (fileName.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase)) return 55;
        return 40;
    }

    static int Depth(string? rel)
    {
        if (rel is null or { Length: 0 }) return 0;
        var n = 0;
        foreach (var c in rel)
        {
            if (c is '/' or '\\') n++;
        }

        return n;
    }

    static bool IsNoiseVertical(string name) =>
        name.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("properties", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("resources", StringComparison.OrdinalIgnoreCase);

    static string? TopSegment(string rel)
    {
        var i = rel.IndexOfAny(['/', '\\']);
        return i < 0 ? null : rel[..i];
    }

    static string Rel(string root, string full)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var f = Path.GetFullPath(full);
        if (f.StartsWith(r, StringComparison.OrdinalIgnoreCase))
        {
            var rel = f[(r.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }

        return Path.GetFileName(full);
    }
}
