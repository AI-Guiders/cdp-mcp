using System.Text.Json;
using System.Text.RegularExpressions;
using Tomlyn;

namespace CdpMcp;

/// <summary>
/// Project-tunable quality gates (agent comfort): pulse + next, not a SOLID sermon.
/// Defaults from host toml; overlay <c>{ProjectRoot}/.cdp/quality-gates.toml</c> wins per gate.
/// </summary>
internal static partial class QualityGates
{
    public const string SchemaVersion = "quality_gates/v0";
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly object CacheLock = new();
    static string? CacheKey;
    static QualityPolicy? CachePolicy;

    public static object EvaluateStore(DocumentBufferStore store, string? projectRoot)
    {
        var policy = LoadEffective(projectRoot);
        if (!policy.Enabled)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                enabled = false,
                pulse = "quality off",
                policy = PolicyCard(policy),
                findings = Array.Empty<object>(),
                next = (object?)null,
                hint = "Enable [quality] in cdp-mcp.toml or .cdp/quality-gates.toml"
            };
        }

        var findings = new List<QualityFinding>();
        foreach (var buf in store.All)
            findings.AddRange(EvaluateBuffer(buf, policy));

        return BuildResult(policy, findings, projectRoot);
    }

    public static object EvaluatePath(DocumentBufferStore store, string? projectRoot, string path)
    {
        var policy = LoadEffective(projectRoot);
        var full = Path.GetFullPath(path);
        DocBuffer? buf = null;
        foreach (var b in store.All)
        {
            if (string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase))
            {
                buf = b;
                break;
            }
        }

        if (buf is null)
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                error = "buffer_not_open",
                path = full,
                hint = "cdp_buffer op=open path= first"
            };
        }

        return BuildResult(policy, EvaluateBuffer(buf, policy).ToList(), projectRoot);
    }

    public static object? ForEditResult(DocBuffer buf, string? projectRoot)
    {
        var policy = LoadEffective(projectRoot);
        if (!policy.Enabled)
            return null;
        var findings = EvaluateBuffer(buf, policy);
        if (findings.Count == 0)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                pulse = "gates ok",
                warn = 0,
                fail = 0,
                findings = Array.Empty<object>(),
                next = SuggestNext(policy, findings, EditSniper.HasHold)
            };
        }

        var warn = findings.Count(f => f.Severity == "warn");
        var fail = findings.Count(f => f.Severity == "fail");
        return new
        {
            schema = SchemaVersion,
            ok = fail == 0,
            pulse = fail > 0 ? $"gates FAIL×{fail}" : $"gates WARN×{warn}",
            warn,
            fail,
            findings = findings.Select(FindingCard).ToArray(),
            next = SuggestNext(policy, findings, EditSniper.HasHold),
            hint = "Project-tunable: .cdp/quality-gates.toml — discuss thresholds, don't ignore pulse."
        };
    }

    public static QualitySnap Snap(DocumentBufferStore store, string? projectRoot)
    {
        var policy = LoadEffective(projectRoot);
        if (!policy.Enabled)
            return new QualitySnap(Enabled: false, Warn: 0, Fail: 0, SuggestSniper: false, Pulse: "off");

        var findings = new List<QualityFinding>();
        foreach (var buf in store.All)
        {
            // Cockpit alert must not thrash on foreign buffers left from a prior project.
            if (projectRoot is { Length: > 0 } pr
                && !DocumentBufferStore.IsUnderProjectRoot(buf.Path, pr))
                continue;
            findings.AddRange(EvaluateBuffer(buf, policy));
        }

        // Sit pulse: unique files with findings — not every method_lines card (×8 noise).
        var warnFiles = findings
            .Where(f => f.Severity == "warn")
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var failFiles = findings
            .Where(f => f.Severity == "fail")
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var suggestSniper = !EditSniper.HasHold && findings.Any(f => f.Id == "suggest_sniper");
        var pulse = failFiles > 0 ? $"FAIL×{failFiles}" : warnFiles > 0 ? $"WARN×{warnFiles}" : "ok";
        return new QualitySnap(true, warnFiles, failFiles, suggestSniper, pulse);
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            CacheKey = null;
            CachePolicy = null;
        }
    }

    static object BuildResult(QualityPolicy policy, List<QualityFinding> findings, string? projectRoot)
    {
        var warn = findings.Count(f => f.Severity == "warn");
        var fail = findings.Count(f => f.Severity == "fail");
        return new
        {
            schema = SchemaVersion,
            ok = fail == 0,
            enabled = true,
            pulse = fail > 0 ? $"gates FAIL×{fail} WARN×{warn}" : warn > 0 ? $"gates WARN×{warn}" : "gates ok",
            warn,
            fail,
            policy = PolicyCard(policy),
            overlay = OverlayPath(projectRoot),
            findings = findings.Select(FindingCard).ToArray(),
            next = SuggestNext(policy, findings, EditSniper.HasHold),
            hint = "Tune via .cdp/quality-gates.toml (project) or [quality] in cdp-mcp.toml (host defaults)."
        };
    }

    static object PolicyCard(QualityPolicy p) => new
    {
        enabled = p.Enabled,
        mode = p.Mode,
        file_lines_warn = p.FileLinesWarn,
        file_lines_fail = p.FileLinesFail,
        method_lines_warn = p.MethodLinesWarn,
        method_lines_fail = p.MethodLinesFail,
        suggest_sniper_file_lines = p.SuggestSniperFileLines,
        source = p.Source
    };

    static object FindingCard(QualityFinding f) => new
    {
        id = f.Id,
        severity = f.Severity,
        path = f.Path,
        symbol = f.Symbol,
        metric = f.Metric,
        value = f.Value,
        threshold = f.Threshold,
        message = f.Message,
        go = f.Go
    };

    static object? SuggestNext(QualityPolicy policy, List<QualityFinding> findings, bool hasHold)
    {
        if (findings.Count == 0)
        {
            if (!hasHold && policy.SuggestSniperFileLines > 0)
                return new { scope = "go=scope — aim before next thick edit", keep = "gates ok" };
            return new { keep = "gates ok — continue" };
        }

        var first = findings.OrderByDescending(f => f.Severity == "fail").ThenBy(f => f.Id).First();
        return new
        {
            primary = first.Go,
            quality = "go=quality / mfd=gates — full findings",
            tune = "edit .cdp/quality-gates.toml if threshold wrong for this project"
        };
    }

    static List<QualityFinding> EvaluateBuffer(DocBuffer buf, QualityPolicy policy)
    {
        var list = new List<QualityFinding>();
        var lines = CountLines(buf.Text);
        var shortPath = ShortPath(buf.Path);

        if (policy.SuggestSniperFileLines > 0
            && lines >= policy.SuggestSniperFileLines
            && (policy.FileLinesWarn <= 0 || lines < policy.FileLinesWarn)
            && !EditSniper.HasHold)
        {
            list.Add(new QualityFinding(
                "suggest_sniper",
                "warn",
                buf.Path,
                null,
                "file_lines",
                lines,
                policy.SuggestSniperFileLines,
                $"{shortPath}: {lines} lines — harness suggests go=scope before thick edit",
                "go=scope"));
        }

        if (policy.FileLinesFail > 0 && lines >= policy.FileLinesFail)
        {
            list.Add(new QualityFinding(
                "file_lines",
                "fail",
                buf.Path,
                null,
                "file_lines",
                lines,
                policy.FileLinesFail,
                $"{shortPath}: {lines} ≥ fail {policy.FileLinesFail}",
                "go=scope → split / extract"));
        }
        else if (policy.FileLinesWarn > 0 && lines >= policy.FileLinesWarn)
        {
            list.Add(new QualityFinding(
                "file_lines",
                "warn",
                buf.Path,
                null,
                "file_lines",
                lines,
                policy.FileLinesWarn,
                $"{shortPath}: {lines} ≥ warn {policy.FileLinesWarn}",
                "go=scope → consider split"));
        }

        if (string.Equals(buf.Language, "csharp", StringComparison.OrdinalIgnoreCase)
            || buf.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || buf.Path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
        {
            QualityFinding? worstMethod = null;
            foreach (var m in ScanCsharpMethods(buf.Text))
            {
                QualityFinding? hit = null;
                if (policy.MethodLinesFail > 0 && m.Lines >= policy.MethodLinesFail)
                {
                    hit = new QualityFinding(
                        "method_lines",
                        "fail",
                        buf.Path,
                        m.Name,
                        "method_lines",
                        m.Lines,
                        policy.MethodLinesFail,
                        $"{shortPath}::{m.Name}: {m.Lines} ≥ fail {policy.MethodLinesFail}",
                        "go=scope from=/till= → extract");
                }
                else if (policy.MethodLinesWarn > 0 && m.Lines >= policy.MethodLinesWarn)
                {
                    hit = new QualityFinding(
                        "method_lines",
                        "warn",
                        buf.Path,
                        m.Name,
                        "method_lines",
                        m.Lines,
                        policy.MethodLinesWarn,
                        $"{shortPath}::{m.Name}: {m.Lines} ≥ warn {policy.MethodLinesWarn}",
                        "go=scope from=/till= → consider extract");
                }

                if (hit is null)
                    continue;
                if (worstMethod is null
                    || hit.Value > worstMethod.Value
                    || (hit.Value == worstMethod.Value && hit.Severity == "fail" && worstMethod.Severity != "fail"))
                    worstMethod = hit;
            }

            if (worstMethod is not null)
                list.Add(worstMethod);
        }

        if (string.Equals(policy.Mode, "warn", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Severity == "fail")
                    list[i] = list[i] with { Severity = "warn", Message = list[i].Message + " (mode=warn → soft)" };
            }
        }

        return list;
    }

    static IEnumerable<(string Name, int Lines)> ScanCsharpMethods(string text)
    {
        // Heuristic: method-like signature then { … matching } — not a full parser.
        var matches = MethodSigRegex().Matches(text);
        foreach (Match m in matches)
        {
            var name = m.Groups["name"].Success ? m.Groups["name"].Value : "method";
            var brace = text.IndexOf('{', m.Index + m.Length - 1);
            if (brace < 0)
                continue;
            var end = MatchingBrace(text, brace);
            if (end < 0)
                continue;
            var slice = text[brace..(end + 1)];
            yield return (name, CountLines(slice));
        }
    }

    static int MatchingBrace(string text, int openIdx)
    {
        var depth = 0;
        for (var i = openIdx; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    static string ShortPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            return string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
        }
        catch
        {
            return path;
        }
    }

    static string? OverlayPath(string? projectRoot) =>
        string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : Path.Combine(projectRoot, ".cdp", "quality-gates.toml");

    public static QualityPolicy LoadEffective(string? projectRoot)
    {
        var hostPath = HostConfigPath();
        var overlay = OverlayPath(projectRoot);
        var hostStamp = File.Exists(hostPath) ? File.GetLastWriteTimeUtc(hostPath).Ticks : 0L;
        var overlayStamp = overlay is not null && File.Exists(overlay)
            ? File.GetLastWriteTimeUtc(overlay).Ticks
            : 0L;
        var key = (projectRoot ?? "") + "|" + hostPath + "|" + hostStamp + "|" + (overlay ?? "") + "|" + overlayStamp;
        lock (CacheLock)
        {
            if (CachePolicy is not null && CacheKey == key)
                return CachePolicy;
        }

        var policy = QualityPolicy.Defaults;
        if (File.Exists(hostPath))
            policy = Merge(policy, TryReadToml(hostPath), "host:" + hostPath);

        if (overlay is not null && File.Exists(overlay))
            policy = Merge(policy, TryReadToml(overlay), "overlay:" + overlay);

        lock (CacheLock)
        {
            CacheKey = key;
            CachePolicy = policy;
        }

        return policy;
    }

    static string HostConfigPath() =>
        Path.Combine(AppContext.BaseDirectory, "config", "cdp-mcp.toml");

    static QualityPolicy Merge(QualityPolicy basePolicy, QualityToml? t, string source)
    {
        if (t?.Quality is null)
            return basePolicy;
        var q = t.Quality;
        var g = q.Gates ?? new QualityTomlGates();
        return basePolicy with
        {
            Enabled = q.Enabled ?? basePolicy.Enabled,
            Mode = string.IsNullOrWhiteSpace(q.Mode) ? basePolicy.Mode : q.Mode!.Trim().ToLowerInvariant(),
            FileLinesWarn = g.FileLinesWarn ?? basePolicy.FileLinesWarn,
            FileLinesFail = g.FileLinesFail ?? basePolicy.FileLinesFail,
            MethodLinesWarn = g.MethodLinesWarn ?? basePolicy.MethodLinesWarn,
            MethodLinesFail = g.MethodLinesFail ?? basePolicy.MethodLinesFail,
            SuggestSniperFileLines = g.SuggestSniperFileLines ?? basePolicy.SuggestSniperFileLines,
            Source = source
        };
    }

    static QualityToml? TryReadToml(string path)
    {
        try
        {
            return TomlSerializer.Deserialize<QualityToml>(
                File.ReadAllText(path),
                new TomlSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, Pretty);

    [GeneratedRegex(
        """(?m)^\s*(?:public|private|protected|internal|static|async|partial|override|virtual|new|sealed|\s)+[\w<>\[\],\s\?]+\s+(?<name>\w+)\s*\([^;]*\)\s*(?:where\s+[\w\s,:<>]+\s*)?\{""",
        RegexOptions.Compiled)]
    private static partial Regex MethodSigRegex();

    public sealed record QualitySnap(bool Enabled, int Warn, int Fail, bool SuggestSniper, string Pulse);

    public sealed record QualityPolicy(
        bool Enabled,
        string Mode,
        int FileLinesWarn,
        int FileLinesFail,
        int MethodLinesWarn,
        int MethodLinesFail,
        int SuggestSniperFileLines,
        string Source)
    {
        public static QualityPolicy Defaults { get; } = new(
            Enabled: true,
            Mode: "warn",
            FileLinesWarn: 400,
            FileLinesFail: 800,
            MethodLinesWarn: 80,
            MethodLinesFail: 150,
            SuggestSniperFileLines: 200,
            Source: "defaults");
    }

    public sealed record QualityFinding(
        string Id,
        string Severity,
        string Path,
        string? Symbol,
        string Metric,
        int Value,
        int Threshold,
        string Message,
        string Go);

    sealed class QualityToml
    {
        public QualityTomlQuality? Quality { get; set; }
    }

    sealed class QualityTomlQuality
    {
        public bool? Enabled { get; set; }
        public string? Mode { get; set; }
        public QualityTomlGates? Gates { get; set; }
    }

    sealed class QualityTomlGates
    {
        public int? FileLinesWarn { get; set; }
        public int? FileLinesFail { get; set; }
        public int? MethodLinesWarn { get; set; }
        public int? MethodLinesFail { get; set; }
        public int? SuggestSniperFileLines { get; set; }
    }
}
