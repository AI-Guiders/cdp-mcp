#nullable enable

namespace CdpMcp;

/// <summary>
/// Tooth: partial-file sprawl that silences per-file <c>file_lines</c> without a real seam.
/// Partial means codegen/hand split of ONE type — not a metric peel mill.
/// </summary>
internal static partial class QualityGates
{
    /// <summary>Skip designer/sourcegen peels — legitimate partial authors.</summary>
    internal static bool IsGeneratedPartialPath(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        var n = path.Replace('\\', '/');
        return n.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
               || n.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Family stem: <c>Foo.Bar.cs</c> / <c>Foo.cs</c> → <c>Foo</c>.</summary>
    internal static string PartialFamilyStem(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    internal static bool IsPartialFamilyMemberName(string fileNameWithoutExt, string stem) =>
        string.Equals(fileNameWithoutExt, stem, StringComparison.OrdinalIgnoreCase)
        || fileNameWithoutExt.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> ListPartialFamilyPaths(string path)
    {
        if (IsGeneratedPartialPath(path))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return Array.Empty<string>();

        var stem = PartialFamilyStem(path);
        if (string.IsNullOrWhiteSpace(stem))
            return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(f => !IsGeneratedPartialPath(f)
                            && IsPartialFamilyMemberName(Path.GetFileNameWithoutExtension(f), stem))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static QualityFinding? TryPartialFamilyFinding(
        string path,
        QualityPolicy policy,
        Func<string, int> lineCount)
    {
        if (policy.PartialFamilyFilesWarn <= 0
            && policy.FileLinesWarn <= 0
            && policy.FileLinesFail <= 0
            && policy.PartialFamilyFilesFail <= 0)
            return null;

        var family = ListPartialFamilyPaths(path);
        if (family.Count < 2)
            return null;

        // Require dotted sibling (Foo.Bar.cs) — avoids Foo.cs + FooTests.cs false friends.
        if (!family.Any(f => Path.GetFileNameWithoutExtension(f).Contains('.')))
            return null;

        var sum = 0;
        foreach (var f in family)
            sum += Math.Max(0, lineCount(f));

        var stem = PartialFamilyStem(path);
        var files = family.Count;

        string? severity = null;
        var threshold = 0;
        string metric;

        if (policy.PartialFamilyFilesFail > 0 && files >= policy.PartialFamilyFilesFail)
        {
            severity = "fail";
            threshold = policy.PartialFamilyFilesFail;
            metric = "partial_family_files";
        }
        else if (policy.FileLinesFail > 0 && sum >= policy.FileLinesFail)
        {
            severity = "fail";
            threshold = policy.FileLinesFail;
            metric = "partial_family_lines";
        }
        else if (policy.PartialFamilyFilesWarn > 0 && files >= policy.PartialFamilyFilesWarn)
        {
            severity = "warn";
            threshold = policy.PartialFamilyFilesWarn;
            metric = "partial_family_files";
        }
        else if (policy.FileLinesWarn > 0 && sum >= policy.FileLinesWarn)
        {
            severity = "warn";
            threshold = policy.FileLinesWarn;
            metric = "partial_family_lines";
        }
        else
            return null;

        var value = metric == "partial_family_files" ? files : sum;
        var hub = family.FirstOrDefault(f =>
                       string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase))
                   ?? family[0];

        return new QualityFinding(
            "partial_family",
            severity,
            hub,
            stem,
            metric,
            value,
            threshold,
            $"{stem}: {files} partial files · {sum} lines — file_lines peel ≠ seam (partial ≠ split); OOA&D/DRY/KISS → real type (partial = narrow)",
            "go=scope → OOA&D extract / discuss exclude — not more SoftFL partials");
    }
}
