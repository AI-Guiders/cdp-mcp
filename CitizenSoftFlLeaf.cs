#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// SoftFL leaf SSOT outside Completions prose.
/// Latch: %LocalAppData%/cdp-mcp/citizen-softfl-leaf-LATEST.json
/// PeerReadyCharge formats from here — Mentions is Current leaf, not charge identity.
/// ApplyArmed = SoftFL apply contour → Mutate/Verify/Deploy blast gate.
/// </summary>
internal static class CitizenSoftFlLeaf
{
    public const string Schema = "citizen_softfl_leaf/v0";

    public const string MentionsDefaultPath =
        "D:/Experiments/Personal Cursor Folder/Financial/software/open/cascade-ide/CascadeIDE.GlassCore/Intercom/GlassIntercomMention.cs";

    public const string MentionsDefaultMutation =
        "wire MentionsAll→ExpandWakes inside ResolveWakes (+ Suggest @all? + tests in GlassIntercomMentionTests)";

    public const string MentionsDefaultDod =
        "MentionsAll expands in ResolveWakes; Suggest @all?; tests green; no take-loop";

    public const int MentionsDigStart = 60;
    public const int MentionsDigEnd = 120;

    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static Leaf? Memory;
    static bool DiskHydrated;
    static bool ApplyArmedMemory;

    public sealed record Leaf(
        string Id,
        string Path,
        string Mutation,
        string Dod,
        int? DigStartLine = null,
        int? DigEndLine = null);

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    /// <summary>Test hook: force Current without disk.</summary>
    internal static Leaf? OverrideForTests { get; set; }

    /// <summary>Test hook: force apply-armed without disk.</summary>
    internal static bool? ApplyArmedOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "citizen-softfl-leaf-LATEST.json");

    public static Leaf MentionsDefault { get; } = new(
        Id: "mentions-all-resolve-wakes",
        Path: MentionsDefaultPath,
        Mutation: MentionsDefaultMutation,
        Dod: MentionsDefaultDod,
        DigStartLine: MentionsDigStart,
        DigEndLine: MentionsDigEnd);

    public static Leaf Current
    {
        get
        {
            if (OverrideForTests is { } ov)
                return ov;
            lock (Gate)
            {
                EnsureHydrated();
                return Memory ?? MentionsDefault;
            }
        }
    }

    /// <summary>
    /// SoftFL apply contour armed — Mutate/Verify/Deploy blast gate active.
    /// Dig stays free. Persisted in leaf latch.
    /// </summary>
    public static bool IsApplyArmed
    {
        get
        {
            if (ApplyArmedOverrideForTests is { } ov)
                return ov;
            if (OverrideForTests is not null)
                return ApplyArmedOverrideForTests ?? false;
            lock (Gate)
            {
                EnsureHydrated();
                return ApplyArmedMemory;
            }
        }
    }

    /// <summary>True when SoftFlLeaf was Seed/EnsureMentionsDefault (not ambient Mentions fallback alone).</summary>
    public static bool HasSeededLeaf
    {
        get
        {
            if (OverrideForTests is not null)
                return true;
            lock (Gate)
            {
                EnsureHydrated();
                return Memory is not null;
            }
        }
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            Memory = null;
            DiskHydrated = true;
            ApplyArmedMemory = false;
            OverrideForTests = null;
            ApplyArmedOverrideForTests = null;
            if (RootOverrideForTests is null)
                return;
            try
            {
                if (File.Exists(LatchPath))
                    File.Delete(LatchPath);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public static void ArmApply(bool persist = true)
    {
        if (OverrideForTests is not null || ApplyArmedOverrideForTests is not null)
        {
            ApplyArmedOverrideForTests = true;
            return;
        }

        lock (Gate)
        {
            EnsureHydrated();
            ApplyArmedMemory = true;
            if (persist)
                PersistLocked();
        }
    }

    public static void DisarmApply(bool persist = true)
    {
        if (OverrideForTests is not null || ApplyArmedOverrideForTests is not null)
        {
            ApplyArmedOverrideForTests = false;
            return;
        }

        lock (Gate)
        {
            EnsureHydrated();
            ApplyArmedMemory = false;
            if (persist)
                PersistLocked();
        }
    }

    public static void Seed(Leaf leaf, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        if (OverrideForTests is not null)
        {
            OverrideForTests = leaf;
            return;
        }

        lock (Gate)
        {
            Memory = leaf;
            DiskHydrated = true;
            if (persist)
                PersistLocked();
        }
    }

    public static void EnsureMentionsDefault(bool persist = true)
    {
        if (OverrideForTests is not null)
            return;
        lock (Gate)
        {
            EnsureHydrated();
            if (Memory is null)
            {
                Memory = MentionsDefault;
                if (persist)
                    PersistLocked();
            }
        }
    }

    public static bool MatchesPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var want = CitizenResultWake.NormalizeTakePath(Current.Path);
        var got = CitizenResultWake.NormalizeTakePath(path);
        return got.Equals(want, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(got).Equals(Path.GetFileName(want), StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatDigTakeIntent(Leaf? leaf = null)
    {
        leaf ??= Current;
        var start = leaf.DigStartLine ?? MentionsDigStart;
        var end = leaf.DigEndLine ?? MentionsDigEnd;
        return "@intent take path=\"" + leaf.Path + "\" start_line=" + start + " end_line=" + end;
    }

    /// <summary>Default peer_ready apply charge — mutation from SSOT, not dig PASTE. Arms blast gate.</summary>
    public static string FormatApplyCharge(Leaf? leaf = null)
    {
        leaf ??= Current;
        ArmApply();
        return "reason=peer_ready — SoftFL apply PASTE from leaf SSOT: "
            + leaf.Mutation
            + " in "
            + leaf.Path
            + ". Do NOT take/read again if file already open. Partner «меняй» = green. find≠next hand; "
            + "do not invent CascadeIDE.cs / *Host.cs / GlassIntercom.cs / dialog-history basenames; "
            + "Radio alone ≠ SoftFL done; Radio only if stuck (one fact). leaf_id="
            + leaf.Id
            + " dod="
            + leaf.Dod
            + ".";
    }

    public static bool IsApplyWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready ", StringComparison.OrdinalIgnoreCase)
        && body.Contains("leaf SSOT", StringComparison.OrdinalIgnoreCase)
        && !body.Trim().StartsWith("reason=peer_ready_dig", StringComparison.OrdinalIgnoreCase)
        && !body.Trim().StartsWith("reason=peer_ready_retry", StringComparison.OrdinalIgnoreCase)
        && !body.Trim().StartsWith("reason=peer_ready_next_open", StringComparison.OrdinalIgnoreCase)
        && !body.Contains("reason=peer_ready_kb", StringComparison.Ordinal);

    static void EnsureHydrated()
    {
        if (DiskHydrated)
            return;
        DiskHydrated = true;
        try
        {
            if (!File.Exists(LatchPath))
            {
                // No ambient Mentions seed — SoftFL leaf must Seed/EnsureMentionsDefault.
                Memory = null;
                ApplyArmedMemory = false;
                return;
            }

            var json = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<LatchDoc>(json, ReadOpts);
            if (doc?.Leaf is { Path.Length: > 0, Mutation.Length: > 0 } leaf)
                Memory = leaf;
            else
                Memory = null;
            ApplyArmedMemory = doc?.ApplyArmed == true;
        }
        catch
        {
            Memory = null;
            ApplyArmedMemory = false;
        }
    }

    static void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new LatchDoc(
                Schema,
                DateTimeOffset.UtcNow,
                Memory ?? MentionsDefault,
                ApplyArmedMemory);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort latch */
        }
    }

    sealed record LatchDoc(string Schema, DateTimeOffset AtUtc, Leaf Leaf, bool ApplyArmed = false);
}
