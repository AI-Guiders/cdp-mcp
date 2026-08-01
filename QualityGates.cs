using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Project-tunable quality gates (agent comfort): pulse + next, not a SOLID sermon.
/// Defaults from host toml; overlay <c>{ProjectRoot}/.cdp/quality-gates.toml</c> wins per gate.
/// </summary>
internal static partial class QualityGates
{
    public const string SchemaVersion = "quality_gates/v0";
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

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
                return new
                {
                    scope = "go=scope — aim before next thick edit",
                    disk = "go=quality scope=disk — project FileLines map (ADX)",
                    keep = "gates ok"
                };
            return new
            {
                keep = "gates ok — continue",
                disk = "go=quality scope=disk — project FileLines / near-miss without shell"
            };
        }

        var first = findings.OrderByDescending(f => f.Severity == "fail").ThenBy(f => f.Id).First();
        return new
        {
            primary = first.Go,
            quality = "go=quality / mfd=gates — full findings",
            disk = "go=quality scope=disk — whole-project FileLines map",
            tune = "edit .cdp/quality-gates.toml if threshold wrong for this project"
        };
    }

    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, Pretty);
}
