using System.Text.Json;
using Tomlyn;

namespace CdpMcp;

internal static partial class QualityGates
{
    static readonly object CacheLock = new();
    static string? CacheKey;
    static QualityPolicy? CachePolicy;

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            CacheKey = null;
            CachePolicy = null;
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
