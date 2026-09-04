#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Explore full-a latch: corr dig (or explicit no_adr+why) before Act on ADR-mapped loci.
/// Being ≠ seeming — Done without latch is theatre.
/// </summary>
internal static class ExploreCorrLatch
{
    public const string Schema = "explore_corr_latch/v0";
    public const string RefuseId = "explore_corr_missing";
    public const string KindCorr = "corr";
    public const string KindNoAdr = "no_adr";

    internal static string? PathOverrideForTests { get; set; }
    internal static bool? EnabledOverrideForTests { get; set; }

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public sealed record Stamp(
        string WorkspaceRoot,
        string FileRel,
        string Kind,
        string? Why,
        int AdrCount,
        DateTimeOffset StampedUtc);

    const int MaxStamps = 50;

    static string DirRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "explore-corr");

    static string FileForRoot(string workspaceRoot)
    {
        if (PathOverrideForTests is { Length: > 0 })
            return PathOverrideForTests;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant())))[..16].ToLowerInvariant();
        return Path.Combine(DirRoot, hash + ".json");
    }

    public static bool IsEnabled()
    {
        if (EnabledOverrideForTests is { } ov)
            return ov;
        var env = Environment.GetEnvironmentVariable("CDP_EXPLORE_CORR");
        if (string.Equals(env, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public static void StampCorr(string workspaceRoot, string fileRel, int adrCount)
    {
        Write(new Stamp(
            NormalizeRoot(workspaceRoot),
            NormalizeRel(fileRel),
            KindCorr,
            Why: null,
            adrCount,
            DateTimeOffset.UtcNow));
    }

    public static void StampNoAdr(string workspaceRoot, string fileRel, string why)
    {
        if (string.IsNullOrWhiteSpace(why))
            throw new ArgumentException("no_adr requires why= (short reason — not empty theatre).");

        Write(new Stamp(
            NormalizeRoot(workspaceRoot),
            NormalizeRel(fileRel),
            KindNoAdr,
            why.Trim(),
            AdrCount: 0,
            DateTimeOffset.UtcNow));
    }

    public static bool TryRead(string workspaceRoot, out Stamp? stamp)
    {
        stamp = null;
        if (!TryReadStamps(workspaceRoot, out var stamps) || stamps.Count == 0)
            return false;
        stamp = stamps.OrderByDescending(s => s.StampedUtc).First();
        return true;
    }

    /// <summary>All live stamps for a workspace root. v0 single-stamp docs migrate transparently.</summary>
    public static bool TryReadStamps(string workspaceRoot, out List<Stamp> stamps)
    {
        stamps = new List<Stamp>();
        try
        {
            var path = FileForRoot(workspaceRoot);
            if (!File.Exists(path))
                return false;
            var doc = TryReadRaw(path);
            if (doc is null || string.IsNullOrWhiteSpace(doc.WorkspaceRoot))
                return false;

            if (doc.Stamps is { Count: > 0 })
            {
                foreach (var s in doc.Stamps)
                {
                    if (!DateTimeOffset.TryParse(s.StampedUtc, out var utc))
                        continue;
                    stamps.Add(new Stamp(
                        doc.WorkspaceRoot,
                        s.FileRel ?? "",
                        s.Kind ?? KindCorr,
                        s.Why,
                        s.AdrCount,
                        utc));
                }

                return stamps.Count > 0;
            }

            // v0 migration: single-stamp doc.
            if (!DateTimeOffset.TryParse(doc.StampedUtc, out var utc0))
                return false;
            stamps.Add(new Stamp(
                doc.WorkspaceRoot,
                doc.FileRel ?? "",
                doc.Kind ?? KindCorr,
                doc.Why,
                doc.AdrCount,
                utc0));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static LatchDoc? TryReadRaw(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<LatchDoc>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static bool IsFresh(Stamp stamp, TimeSpan? maxAge = null)
    {
        var age = maxAge ?? TimeSpan.FromHours(8);
        return DateTimeOffset.UtcNow - stamp.StampedUtc <= age;
    }

    public static bool MatchesLocus(Stamp stamp, string mutateRel)
    {
        var stamped = NormalizeRel(stamp.FileRel);
        var target = NormalizeRel(mutateRel);
        if (stamped.Length == 0 || target.Length == 0)
            return false;
        if (string.Equals(stamped, target, StringComparison.OrdinalIgnoreCase))
            return true;

        // Redundant root prefix on the stamp (path passed relative to an outer root):
        // match when the target rel is a suffix of the stamped rel (or vice versa) on a path boundary.
        if (target.Length > stamped.Length && target.EndsWith("/" + stamped, StringComparison.OrdinalIgnoreCase))
            return true;
        if (stamped.Length > target.Length && stamped.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase))
            return true;

        // Directory prefix latch (map keys / folder dig).
        var prefix = stamped.EndsWith('/') ? stamped : stamped + "/";
        if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        // Same directory as stamped file. Root-level files (no '/') are siblings
        // of every other root-level file — the root dir IS their directory.
        var stampedDir = stamped.Contains('/') ? stamped[..(stamped.LastIndexOf('/') + 1)] : "";
        if (stampedDir.Length > 0)
            return target.StartsWith(stampedDir, StringComparison.OrdinalIgnoreCase);
        if (!target.Contains('/') && stamped.Contains('/') == false)
            return true;

        return false;
    }

    public static bool HasSatisfied(string workspaceRoot, string mutateRel, TimeSpan? maxAge = null)
        => TryReadStamps(workspaceRoot, out var stamps)
           && stamps.Any(s =>
               IsFresh(s, maxAge)
               && MatchesLocus(s, mutateRel)
               && (s.Kind is KindCorr or KindNoAdr)
               && (s.Kind != KindNoAdr || !string.IsNullOrWhiteSpace(s.Why)));

    public static bool HasAnyFresh(string workspaceRoot, TimeSpan? maxAge = null)
        => TryReadStamps(workspaceRoot, out var stamps)
           && stamps.Any(s => IsFresh(s, maxAge));

    /// <summary>True when locus has forward ADR/docs in workspace.toml — gate should arm.</summary>
    public static bool HasMappedAdrs(string absPath, string? rootHint)
    {
        var result = WorkspaceCorrespondence.TryResolve(absPath, rootHint);
        return result is { ForwardDocs.Length: > 0 };
    }

    public static string? FindWorkspaceRoot(string absPath, string? rootHint)
        => WorkspaceCorrespondence.FindWorkspaceRoot(absPath, rootHint);

    public static object Pulse(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !TryRead(workspaceRoot, out var stamp) || stamp is null)
        {
            return new
            {
                schema = Schema,
                ok = false,
                armed = IsEnabled(),
                hint = "No explore-corr latch — cdp_analysis_scene feature=correspondence path= or feature=no_adr why="
            };
        }

        return new
        {
            schema = Schema,
            ok = true,
            armed = IsEnabled(),
            kind = stamp.Kind,
            file = stamp.FileRel,
            why = stamp.Why,
            adr_count = stamp.AdrCount,
            stamped_utc = stamp.StampedUtc.ToString("o"),
            fresh = IsFresh(stamp),
            workspace_root = stamp.WorkspaceRoot
        };
    }

    public static void Clear()
    {
        try
        {
            if (PathOverrideForTests is { Length: > 0 } && File.Exists(PathOverrideForTests))
                File.Delete(PathOverrideForTests);
        }
        catch
        {
            /* best-effort */
        }
    }

    static void Write(Stamp stamp)
    {
        try
        {
            var path = FileForRoot(stamp.WorkspaceRoot);
            var dir = Path.GetDirectoryName(path);
            if (dir is { Length: > 0 })
                Directory.CreateDirectory(dir);

            // v1: stamps list per workspace root (append/update per file; oldest evicted at cap).
            // v0 docs (single FileRel) are migrated on read by TryRead.
            var doc = TryReadRaw(path) ?? new LatchDoc
            {
                Schema = Schema,
                WorkspaceRoot = stamp.WorkspaceRoot
            };
            doc.WorkspaceRoot = stamp.WorkspaceRoot;
            doc.Stamps ??= new List<LatchStamp>();
            doc.Stamps.RemoveAll(s =>
                string.Equals(NormalizeRel(s.FileRel ?? ""), NormalizeRel(stamp.FileRel), StringComparison.OrdinalIgnoreCase));
            doc.Stamps.Add(new LatchStamp
            {
                FileRel = stamp.FileRel,
                Kind = stamp.Kind,
                Why = stamp.Why,
                AdrCount = stamp.AdrCount,
                StampedUtc = stamp.StampedUtc.ToString("o")
            });
            while (doc.Stamps.Count > MaxStamps)
                doc.Stamps.RemoveAt(0);

            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOpts));
        }
        catch
        {
            /* best-effort */
        }
    }

    static string NormalizeRoot(string root) =>
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    static string NormalizeRel(string rel) =>
        rel.Replace('\\', '/').Trim().TrimStart('/');

    sealed class LatchDoc
    {
        public string? Schema { get; set; }
        public string? WorkspaceRoot { get; set; }

        // v0 single-stamp fields (legacy, read for migration).
        public string? FileRel { get; set; }
        public string? Kind { get; set; }
        public string? Why { get; set; }
        public int AdrCount { get; set; }
        public string? StampedUtc { get; set; }

        // v1: per-file stamps.
        public List<LatchStamp>? Stamps { get; set; }
    }

    sealed class LatchStamp
    {
        public string? FileRel { get; set; }
        public string? Kind { get; set; }
        public string? Why { get; set; }
        public int AdrCount { get; set; }
        public string? StampedUtc { get; set; }
    }
}
