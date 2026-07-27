#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=onboard_desk</c> / Meta <c>cdp_onboard</c> — cold-start explore pulse
/// for an open <see cref="SessionContext.ProjectRoot"/> (no ADR required).
/// Not a VS Code Map: entrypoints + top folders + verticals + next[].
/// </summary>
internal static class IdeOnboardChannel
{
    public const string SchemaVersion = "onboard/v0";
    public const string ToolName = "cdp_onboard";
    public const string GoName = "onboard_desk";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly object Gate = new();

    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules", ".vs", "packages", "dist", "out",
        "TestResults", ".idea", ".cascade-ide", "publish-release", "publish-debug",
        ".next", "coverage", "artifacts"
    };

    static readonly Regex EntrypointName = new(
        @"^(Program|Startup|Bootstrap|CompositionRoot)|Host|MainWindow|App\.(axaml|xaml)\.cs$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed class ScanDoc
    {
        public string Schema { get; set; } = SchemaVersion;
        public string Title { get; set; } = "onboard";
        public string? ProjectName { get; set; }
        public string? Root { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string ProfileHint { get; set; } = "unknown";
        public DocsHint Docs { get; set; } = new();
        public List<Hit> Entrypoints { get; set; } = [];
        public List<FolderHit> TopFolders { get; set; } = [];
        public List<VerticalHit> Verticals { get; set; } = [];
        public List<string> Solutions { get; set; } = [];
        public int CsprojCount { get; set; }
        public int FilesScanned { get; set; }
        public bool Truncated { get; set; }
    }

    public sealed class DocsHint
    {
        public bool HasReadme { get; set; }
        public bool HasDocsDir { get; set; }
        public int AdrCount { get; set; }
        public string? ReadmePath { get; set; }
    }

    public sealed class Hit
    {
        public string Kind { get; set; } = "entrypoint";
        public string Label { get; set; } = "";
        public string? Path { get; set; }
        public string? Anchor { get; set; }
        public int Score { get; set; }
    }

    public sealed class FolderHit
    {
        public string Path { get; set; } = "";
        public int FileCount { get; set; }
    }

    public sealed class VerticalHit
    {
        public string Name { get; set; } = "";
        public int FileCount { get; set; }
        public string? SamplePath { get; set; }
        public string? SampleAnchor { get; set; }
    }

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scan" or "refresh" or "rescan" => Scan(session),
            "clear" => Clear(session),
            _ => Scene(session)
        };
    }

    public static string PulseLine(SessionContext session)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            return Pulse(doc);
        }
    }

    public static bool HasScan(SessionContext session)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            return doc.Entrypoints.Count > 0 || doc.Verticals.Count > 0;
        }
    }

    static object Scene(SessionContext session)
    {
        var doc = Load(session);
        if (doc.Entrypoints.Count == 0 && doc.Verticals.Count == 0 &&
            (session.ProjectRoot ?? session.ScmRoot) is { Length: > 0 })
            return Scan(session);
        return OkCard(session, doc, "scene");
    }

    static object Scan(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is null or { Length: 0 })
            return Err("project_required", "cdp_open a project first — onboard scans that ProjectRoot");

        root = Path.GetFullPath(root);
        var doc = BuildScan(root);
        Save(session, doc);
        return OkCard(session, doc, "scan");
    }

    static object Clear(SessionContext session)
    {
        lock (Gate)
        {
            var path = LatestPath(session);
            if (File.Exists(path))
                File.Delete(path);
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "clear",
            pulse = "onboard · cleared",
            hint = "op=scan to rebuild"
        };
    }

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

    static string Pulse(ScanDoc doc)
    {
        if (doc.ProjectName is null or { Length: 0 })
            return "onboard · empty";
        return
            $"onboard · {doc.ProjectName} · {doc.ProfileHint} · entry={doc.Entrypoints.Count} · vert={doc.Verticals.Count} · docs={(doc.Docs.HasReadme || doc.Docs.AdrCount > 0 ? "yes" : "no")}";
    }

    static object OkCard(SessionContext session, ScanDoc doc, string op)
    {
        var next = BuildNext(doc);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op,
            pulse = Pulse(doc),
            detail = "full",
            board_path = LatestPath(session),
            profile_hint = doc.ProfileHint,
            project = doc.ProjectName,
            root = doc.Root,
            docs = new
            {
                has_readme = doc.Docs.HasReadme,
                readme_path = doc.Docs.ReadmePath,
                has_docs_dir = doc.Docs.HasDocsDir,
                adr_count = doc.Docs.AdrCount
            },
            entrypoints = doc.Entrypoints.Select(e => new
            {
                label = e.Label,
                path = e.Path,
                anchor = e.Anchor,
                score = e.Score
            }),
            top_folders = doc.TopFolders.Select(f => new { path = f.Path, file_count = f.FileCount }),
            verticals = doc.Verticals.Select(v => new
            {
                name = v.Name,
                file_count = v.FileCount,
                sample_path = v.SamplePath,
                sample_anchor = v.SampleAnchor
            }),
            solutions = doc.Solutions,
            csproj_count = doc.CsprojCount,
            files_scanned = doc.FilesScanned,
            truncated = doc.Truncated,
            updated_utc = doc.UpdatedUtc,
            next,
            hint = "Cold-start map — not Code Map. Open entrypoint → find_usages → one vertical. op=as_built when profile_hint=cide|cdp_desk."
        };
    }

    static List<object> BuildNext(ScanDoc doc)
    {
        var next = new List<object>();
        if (doc.Entrypoints.Count > 0)
        {
            var e = doc.Entrypoints[0];
            next.Add(new
            {
                go = "buffer",
                label = $"Open {e.Label}",
                why = $"op=open path={e.Path}"
            });
            if (e.Anchor is { Length: > 0 })
            {
                next.Add(new
                {
                    go = "goto",
                    label = $"Land {e.Label}",
                    why = $"anchor={e.Anchor}"
                });
            }
        }

        if (doc.Verticals.Count > 0)
        {
            var v = doc.Verticals[0];
            next.Add(new
            {
                go = "find_desk",
                label = $"Search in {v.Name}",
                why = $"query={v.Name} — pick a type, then find_usages"
            });
        }

        if (doc.ProfileHint is "cide" or "cdp_desk")
        {
            next.Add(new
            {
                go = "arch_desk",
                label = "As-built layers",
                why = "op=as_built — ontological board for known profile"
            });
        }

        if (doc.Docs.HasReadme && doc.Docs.ReadmePath is { Length: > 0 } rm)
        {
            next.Add(new
            {
                go = "buffer",
                label = "Open README",
                why = $"op=open path={rm}"
            });
        }

        next.Add(new { go = GoName, label = "Rescan", why = "op=scan" });
        next.Add(new { go = "layout", label = "Layout onboard", why = "cmd=\"layout onboard\" — M=onboard_desk" });
        return next;
    }

    static ScanDoc Load(SessionContext session)
    {
        lock (Gate)
            return LoadUnlocked(session);
    }

    static void Save(SessionContext session, ScanDoc doc)
    {
        lock (Gate)
        {
            doc.UpdatedUtc = DateTimeOffset.UtcNow;
            doc.Schema = SchemaVersion;
            var dir = BoardDir(session);
            Directory.CreateDirectory(dir);
            File.WriteAllText(LatestPath(session), JsonSerializer.Serialize(doc, Pretty));
        }
    }

    static ScanDoc LoadUnlocked(SessionContext session)
    {
        var path = LatestPath(session);
        if (!File.Exists(path))
            return new ScanDoc();
        try
        {
            return JsonSerializer.Deserialize<ScanDoc>(File.ReadAllText(path), Pretty) ?? new ScanDoc();
        }
        catch
        {
            return new ScanDoc();
        }
    }

    static string LatestPath(SessionContext session) =>
        Path.Combine(BoardDir(session), "LATEST.json");

    static string BoardDir(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is { Length: > 0 })
            return Path.GetFullPath(Path.Combine(root, ".cdp", "onboard"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "onboard");
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()?.Trim(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static object Err(string error, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        go = GoName,
        tool = ToolName,
        error,
        hint
    };
}
