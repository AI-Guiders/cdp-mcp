namespace CdpMcp;

/// <summary>Disk FileLines map for ADX — project scan without shell Measure-Object.</summary>
internal static partial class QualityGates
{
    const int DiskMapDefaultLimit = 40;
    const int DiskMapMaxLimit = 120;

    /// <summary>
    /// Scan <c>*.cs</c> under project root (skip bin/obj/…).
    /// Emits warn/fail <c>file_lines</c> plus near-miss band (warn−50‥warn−1) as info.
    /// Cheap line counts only — no method scan (token tax).
    /// </summary>
    public static object EvaluateDisk(string? projectRoot, int limit = DiskMapDefaultLimit)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                error = "project_root_missing",
                scope = "disk",
                hint = "cdp_open / session project_root first — then go=quality scope=disk"
            };
        }

        var policy = LoadEffective(projectRoot);
        if (!policy.Enabled)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                enabled = false,
                scope = "disk",
                pulse = "quality off",
                policy = PolicyCard(policy),
                findings = Array.Empty<object>(),
                next = (object?)null,
                hint = "Enable [quality] in cdp-mcp.toml or .cdp/quality-gates.toml"
            };
        }

        var cap = Math.Clamp(limit <= 0 ? DiskMapDefaultLimit : limit, 1, DiskMapMaxLimit);
        var nearFloor = NearMissFloor(policy);
        var findings = new List<QualityFinding>();
        var scanned = 0;

        foreach (var path in EnumerateCsFiles(projectRoot))
        {
            scanned++;
            var lines = QuietLineCount(path);
            if (lines <= 0)
                continue;

            var hit = ClassifyDiskFile(path, lines, policy, nearFloor);
            if (hit is not null)
                findings.Add(hit);
        }

        findings.Sort((a, b) =>
        {
            var sev = SevRank(b.Severity).CompareTo(SevRank(a.Severity));
            if (sev != 0) return sev;
            return b.Value.CompareTo(a.Value);
        });

        var truncated = findings.Count > cap;
        if (truncated)
            findings = findings.Take(cap).ToList();

        var warn = findings.Count(f => f.Severity == "warn");
        var fail = findings.Count(f => f.Severity == "fail");
        var near = findings.Count(f => f.Id == "file_lines_near_miss");
        var pulse =
            fail > 0 ? $"disk FAIL×{fail} WARN×{warn} near×{near}"
            : warn > 0 ? $"disk WARN×{warn} near×{near}"
            : near > 0 ? $"disk near×{near}"
            : "disk ok";

        return new
        {
            schema = SchemaVersion,
            ok = fail == 0,
            enabled = true,
            scope = "disk",
            pulse,
            warn,
            fail,
            near_miss = near,
            near_miss_floor = nearFloor,
            scanned,
            shown = findings.Count,
            truncated,
            policy = PolicyCard(policy),
            overlay = OverlayPath(projectRoot),
            findings = findings.Select(FindingCard).ToArray(),
            next = SuggestDiskNext(policy, findings),
            hint = "ADX: prefer scope=disk over shell line counts. Default go=quality stays open-buffers."
        };
    }

    static int NearMissFloor(QualityPolicy policy)
    {
        if (policy.SuggestSniperFileLines > 0)
            return policy.SuggestSniperFileLines;
        if (policy.FileLinesWarn <= 0)
            return 0;
        return Math.Max(1, policy.FileLinesWarn - 50);
    }

    static QualityFinding? ClassifyDiskFile(string path, int lines, QualityPolicy policy, int nearFloor)
    {
        var shortPath = ShortPath(path);
        if (policy.FileLinesFail > 0 && lines >= policy.FileLinesFail)
        {
            return new QualityFinding(
                "file_lines",
                "fail",
                path,
                null,
                "file_lines",
                lines,
                policy.FileLinesFail,
                $"{shortPath}: {lines} ≥ fail {policy.FileLinesFail}",
                "go=scope → split / extract");
        }

        if (policy.FileLinesWarn > 0 && lines >= policy.FileLinesWarn)
        {
            return new QualityFinding(
                "file_lines",
                "warn",
                path,
                null,
                "file_lines",
                lines,
                policy.FileLinesWarn,
                $"{shortPath}: {lines} ≥ warn {policy.FileLinesWarn}",
                "go=scope → consider peel");
        }

        if (nearFloor > 0
            && lines >= nearFloor
            && (policy.FileLinesWarn <= 0 || lines < policy.FileLinesWarn))
        {
            return new QualityFinding(
                "file_lines_near_miss",
                "info",
                path,
                null,
                "file_lines",
                lines,
                nearFloor,
                $"{shortPath}: {lines} near-miss (≥{nearFloor}, <warn {policy.FileLinesWarn})",
                "go=scope → peel before warn");
        }

        return null;
    }

    static object SuggestDiskNext(QualityPolicy policy, List<QualityFinding> findings)
    {
        if (findings.Count == 0)
            return new { keep = "disk ok — no FileLines hits", buffers = "go=quality — open buffers only" };

        var first = findings
            .OrderByDescending(f => SevRank(f.Severity))
            .ThenByDescending(f => f.Value)
            .First();
        return new
        {
            primary = first.Go,
            open = $"cdp_buffer op=open path={first.Path}",
            quality = "go=quality — buffer methods after open",
            tune = "edit .cdp/quality-gates.toml if threshold wrong"
        };
    }

    static int SevRank(string severity) => severity switch
    {
        "fail" => 3,
        "warn" => 2,
        "info" => 1,
        _ => 0
    };

    static IEnumerable<string> EnumerateCsFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var n = f.Replace('\\', '/');
                    return !n.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("/.vs/", StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("/publish", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            yield break;
        }

        foreach (var f in files)
            yield return f;
    }

    static int QuietLineCount(string path)
    {
        try
        {
            var n = 0;
            foreach (var _ in File.ReadLines(path))
            {
                n++;
                if (n > 20000) break;
            }

            return n;
        }
        catch
        {
            return 0;
        }
    }
}
