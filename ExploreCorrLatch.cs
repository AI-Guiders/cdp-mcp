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
        try
        {
            var path = FileForRoot(workspaceRoot);
            if (!File.Exists(path))
                return false;
            var doc = JsonSerializer.Deserialize<LatchDoc>(File.ReadAllText(path));
            if (doc is null || string.IsNullOrWhiteSpace(doc.WorkspaceRoot))
                return false;
            if (!DateTimeOffset.TryParse(doc.StampedUtc, out var utc))
                return false;
            stamp = new Stamp(
                doc.WorkspaceRoot,
                doc.FileRel ?? "",
                doc.Kind ?? KindCorr,
                doc.Why,
                doc.AdrCount,
                utc);
            return true;
        }
        catch
        {
            return false;
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

        // Same directory as stamped file.
        var stampedDir = stamped.Contains('/') ? stamped[..(stamped.LastIndexOf('/') + 1)] : "";
        if (stampedDir.Length > 0 && target.StartsWith(stampedDir, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool HasSatisfied(string workspaceRoot, string mutateRel, TimeSpan? maxAge = null)
        => TryRead(workspaceRoot, out var stamp)
           && stamp is not null
           && IsFresh(stamp, maxAge)
           && MatchesLocus(stamp, mutateRel)
           && (stamp.Kind is KindCorr or KindNoAdr)
           && (stamp.Kind != KindNoAdr || !string.IsNullOrWhiteSpace(stamp.Why));

    public static bool HasAnyFresh(string workspaceRoot, TimeSpan? maxAge = null)
        => TryRead(workspaceRoot, out var stamp)
           && stamp is not null
           && IsFresh(stamp, maxAge);

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
            var doc = new LatchDoc
            {
                Schema = Schema,
                WorkspaceRoot = stamp.WorkspaceRoot,
                FileRel = stamp.FileRel,
                Kind = stamp.Kind,
                Why = stamp.Why,
                AdrCount = stamp.AdrCount,
                StampedUtc = stamp.StampedUtc.ToString("o")
            };
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
        public string? FileRel { get; set; }
        public string? Kind { get; set; }
        public string? Why { get; set; }
        public int AdrCount { get; set; }
        public string? StampedUtc { get; set; }
    }
}
