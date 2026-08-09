#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Enforceable scar ledger — lived SoftFL/domain experience compiled into host refuse ids.
/// Domain card stays voice; this latch is muscle after compact.
/// Latch: %LocalAppData%/cdp-mcp/citizen-scar-ledger-LATEST.json
/// </summary>
internal static class CitizenScarLedger
{
    public const string Schema = "citizen_scar_ledger/v0";

    public const string ScarPathMutateOffLeaf = "path_mutate_off_leaf";
    public const string ScarMutateWithoutLeaf = "mutate_without_leaf";
    public const string ScarVerifyDeployWithoutLeaf = "verify_deploy_without_leaf";
    public const string ScarDigClosesSoftFl = "dig_closes_softfl";

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

    static Dictionary<string, Scar>? Memory;
    static bool DiskHydrated;

    public sealed record Scar(
        string Id,
        string RefuseId,
        string Line,
        string Source,
        bool Armed = true,
        string? LeafId = null,
        DateTimeOffset? ArmedUtc = null);

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "citizen-scar-ledger-LATEST.json");

    public static void ResetForTests()
    {
        lock (Gate)
        {
            Memory = null;
            DiskHydrated = true;
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

    public static void EnsureBuiltins(bool persist = true)
    {
        lock (Gate)
        {
            EnsureHydrated();
            Memory ??= new Dictionary<string, Scar>(StringComparer.OrdinalIgnoreCase);
            foreach (var scar in BuiltinScars())
            {
                if (!Memory.ContainsKey(scar.Id))
                    Memory[scar.Id] = scar;
            }

            if (persist)
                PersistLocked();
        }
    }

    public static bool IsArmed(string scarId)
    {
        lock (Gate)
        {
            EnsureHydrated();
            EnsureBuiltins(persist: false);
            return Memory is not null
                && Memory.TryGetValue(scarId, out var scar)
                && scar.Armed;
        }
    }

    /// <summary>Dogfood burn → promote/refresh one refuse line into the ledger.</summary>
    public static Scar Promote(
        string id,
        string refuseId,
        string line,
        string source = "dogfood",
        string? leafId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(refuseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        lock (Gate)
        {
            EnsureHydrated();
            Memory ??= new Dictionary<string, Scar>(StringComparer.OrdinalIgnoreCase);
            var scar = new Scar(
                Id: id.Trim(),
                RefuseId: refuseId.Trim(),
                Line: line.Trim(),
                Source: string.IsNullOrWhiteSpace(source) ? "dogfood" : source.Trim(),
                Armed: true,
                LeafId: leafId,
                ArmedUtc: DateTimeOffset.UtcNow);
            Memory[scar.Id] = scar;
            PersistLocked();
            return scar;
        }
    }

    public static IReadOnlyList<Scar> Snapshot()
    {
        lock (Gate)
        {
            EnsureHydrated();
            EnsureBuiltins(persist: false);
            return Memory is null
                ? []
                : Memory.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    static IEnumerable<Scar> BuiltinScars()
    {
        var at = DateTimeOffset.UtcNow;
        yield return new Scar(
            ScarPathMutateOffLeaf,
            CitizenScarGate.RefusePathMutateOffLeaf,
            "SoftFL apply armed: PathMutate path must match SoftFlLeaf SSOT (force= escape)",
            "builtin",
            Armed: true,
            ArmedUtc: at);
        yield return new Scar(
            ScarMutateWithoutLeaf,
            CitizenScarGate.RefuseMutateWithoutLeaf,
            "SoftFL apply armed: Mutate/Verify/Deploy need seeded SoftFlLeaf (force= escape)",
            "builtin",
            Armed: true,
            ArmedUtc: at);
        yield return new Scar(
            ScarVerifyDeployWithoutLeaf,
            CitizenScarGate.RefuseVerifyDeployWithoutLeaf,
            "SoftFL apply armed: Verify/Deploy without SoftFlLeaf refused (force= escape)",
            "builtin",
            Armed: true,
            ArmedUtc: at);
        yield return new Scar(
            ScarDigClosesSoftFl,
            "scar_dig_closes_softfl",
            "Dig|Radio under SoftFL apply ≠ SoftFL done; peer_ship only Mutate∩leaf",
            "builtin",
            Armed: true,
            ArmedUtc: at);
    }

    static void EnsureHydrated()
    {
        if (DiskHydrated)
            return;
        DiskHydrated = true;
        Memory = new Dictionary<string, Scar>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(LatchPath))
                return;

            var json = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<LatchDoc>(json, ReadOpts);
            if (doc?.Scars is null)
                return;
            foreach (var scar in doc.Scars)
            {
                if (scar is { Id.Length: > 0, RefuseId.Length: > 0 })
                    Memory[scar.Id] = scar;
            }
        }
        catch
        {
            /* best-effort */
        }
    }

    static void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var scars = Memory?.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            var doc = new LatchDoc(Schema, DateTimeOffset.UtcNow, scars);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort latch */
        }
    }

    sealed record LatchDoc(string Schema, DateTimeOffset AtUtc, IReadOnlyList<Scar> Scars);
}
