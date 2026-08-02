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

}
