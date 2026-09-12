#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// SoftFL apply latch — scoped target path + mutation prose + apply-armed blast gate.
/// Not TaskManager/IntentWorkspace "leaf" (tree node); domain "leaf" lives in TM only.
/// Persisted: <c>%LocalAppData%/cdp-mcp/citizen-softfl-apply-latch.json</c>
/// Legacy read: <c>citizen-softfl-leaf-LATEST.json</c>.
/// </summary>
internal static class CitizenSoftFlApplyLatch
{
    public const string SchemaV1 = "citizen_softfl_apply_latch/v1";
    public const string LegacySchema = "citizen_softfl_leaf/v0";
    public const string LatchFileName = "citizen-softfl-apply-latch.json";
    public const string LegacyLatchFileName = "citizen-softfl-leaf-LATEST.json";

    /// <summary>Dogfood fallback when latch empty — not operator config.</summary>
    public const string DogfoodMentionsPath =
        "D:/Experiments/Personal Cursor Folder/Financial/software/open/cascade-ide/CascadeIDE.GlassCore/Intercom/GlassIntercomMention.cs";

    public const string DogfoodMentionsMutation =
        "wire MentionsAll→ExpandWakes inside ResolveWakes (+ Suggest @all? + tests in GlassIntercomMentionTests)";

    public const string DogfoodMentionsDod =
        "MentionsAll expands in ResolveWakes; Suggest @all?; tests green; no take-loop";

    public const int DogfoodMentionsDigStart = 60;
    public const int DogfoodMentionsDigEnd = 120;

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

    static ApplyScope? Memory;
    static bool DiskHydrated;
    static bool ApplyArmedMemory;

    /// <summary>Scoped SoftFL apply target (path, mutation intent, DoD).</summary>
    public sealed record ApplyScope(
        string Id,
        string Path,
        string Mutation,
        string Dod,
        int? DigStartLine = null,
        int? DigEndLine = null);

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    /// <summary>Test hook: force Current without disk.</summary>
    internal static ApplyScope? OverrideForTests { get; set; }

    /// <summary>Test hook: force apply-armed without disk.</summary>
    internal static bool? ApplyArmedOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, LatchFileName);

    static string LegacyLatchPath => Path.Combine(StateRoot, LegacyLatchFileName);

    public static ApplyScope DefaultDogfoodScope { get; } = new(
        Id: "mentions-all-resolve-wakes",
        Path: DogfoodMentionsPath,
        Mutation: DogfoodMentionsMutation,
        Dod: DogfoodMentionsDod,
        DigStartLine: DogfoodMentionsDigStart,
        DigEndLine: DogfoodMentionsDigEnd);

    public static ApplyScope Current
    {
        get
        {
            if (OverrideForTests is { } ov)
                return ov;
            lock (Gate)
            {
                EnsureHydrated();
                return Memory ?? DefaultDogfoodScope;
            }
        }
    }

    /// <summary>
    /// SoftFL apply contour armed — Mutate/Verify/Deploy blast gate active.
    /// Dig stays free. Persisted in apply latch.
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

    /// <summary>True when scope was Seed/EnsureDefaultScope (not ambient dogfood fallback alone).</summary>
    public static bool HasPersistedScope
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
            DiskHydrated = false;
            ApplyArmedMemory = false;
            OverrideForTests = null;
            ApplyArmedOverrideForTests = null;
            if (RootOverrideForTests is null)
                return;
            foreach (var path in new[] { LatchPath, LegacyLatchPath })
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }

    /// <summary>Test hook — drop in-memory cache and re-read latch from disk.</summary>
    internal static void ReloadFromDiskForTests()
    {
        lock (Gate)
        {
            DiskHydrated = false;
            Memory = null;
            ApplyArmedMemory = false;
            EnsureHydrated();
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

    public static void Seed(ApplyScope scope, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (OverrideForTests is not null)
        {
            OverrideForTests = scope;
            return;
        }

        lock (Gate)
        {
            Memory = scope;
            DiskHydrated = true;
            if (persist)
                PersistLocked();
        }
    }

    public static void EnsureDefaultScope(bool persist = true)
    {
        if (OverrideForTests is not null)
            return;
        lock (Gate)
        {
            EnsureHydrated();
            if (Memory is null)
            {
                Memory = DefaultDogfoodScope;
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

    public static string FormatDigTakeIntent(ApplyScope? scope = null)
    {
        scope ??= Current;
        var start = scope.DigStartLine ?? DogfoodMentionsDigStart;
        var end = scope.DigEndLine ?? DogfoodMentionsDigEnd;
        return "@intent take path=\"" + scope.Path + "\" start_line=" + start + " end_line=" + end;
    }

    /// <summary>Default peer_ready apply charge — mutation from SSOT, not dig PASTE. Arms blast gate.</summary>
    public static string FormatApplyCharge(ApplyScope? scope = null)
    {
        scope ??= Current;
        ArmApply();
        return "reason=peer_ready — SoftFL apply PASTE from apply latch SSOT: "
            + scope.Mutation
            + " in "
            + scope.Path
            + ". Do NOT take/read again if file already open. Partner «меняй» = green. find≠next hand; "
            + "do not invent CascadeIDE.cs / *Host.cs / GlassIntercom.cs / dialog-history basenames; "
            + "Radio alone ≠ SoftFL done; Radio only if stuck (one fact). scope_id="
            + scope.Id
            + " dod="
            + scope.Dod
            + ".";
    }

    public static bool IsApplyWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready ", StringComparison.OrdinalIgnoreCase)
        && (body.Contains("apply latch SSOT", StringComparison.OrdinalIgnoreCase)
            || body.Contains("leaf SSOT", StringComparison.OrdinalIgnoreCase))
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
            var path = File.Exists(LatchPath) ? LatchPath
                : File.Exists(LegacyLatchPath) ? LegacyLatchPath
                : null;
            if (path is null)
            {
                Memory = null;
                ApplyArmedMemory = false;
                return;
            }

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<LatchDoc>(json, ReadOpts);
            if (doc?.EffectiveScope is { Path.Length: > 0, Mutation.Length: > 0 } scope)
                Memory = scope;
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
            var scope = Memory ?? DefaultDogfoodScope;
            var doc = new LatchDoc
            {
                Schema = SchemaV1,
                AtUtc = DateTimeOffset.UtcNow,
                Scope = scope,
                ApplyArmed = ApplyArmedMemory
            };
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
            File.Move(tmp, LatchPath, overwrite: true);
            try
            {
                if (File.Exists(LegacyLatchPath))
                    File.Delete(LegacyLatchPath);
            }
            catch
            {
                /* best-effort legacy cleanup */
            }
        }
        catch
        {
            /* best-effort latch */
        }
    }

    sealed class LatchDoc
    {
        public string Schema { get; init; } = "";
        public DateTimeOffset AtUtc { get; init; }
        public ApplyScope? Scope { get; init; }

        [JsonPropertyName("leaf")]
        public ApplyScope? LegacyLeaf { get; init; }

        public bool ApplyArmed { get; init; }

        public ApplyScope? EffectiveScope =>
            Scope is { Path.Length: > 0, Mutation.Length: > 0 } s ? s
            : LegacyLeaf is { Path.Length: > 0, Mutation.Length: > 0 } l ? l
            : null;
    }
}
