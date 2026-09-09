#nullable enable
using static CdpMcp.IdeIgniteArmHost;
using System.Text.Json;

namespace CdpMcp;

internal sealed partial class CdpIgniteArmHost
{
    void EnsureLoaded()
    {
        lock (Gate)
        {
            if (Loaded) return;
            Loaded = true;
            TryMigrateLegacyUnlocked();
            if (!File.Exists(StorePath)) return;
            try
            {
                var doc = JsonSerializer.Deserialize<ArmStoreDoc>(File.ReadAllText(StorePath), JsonOpts);
                if (doc?.Arms is { Count: > 0 })
                    Arms = doc.Arms;
            }
            catch
            {
                Arms = [];
            }
        }
    }

    /// <summary>
    /// One-shot: legacy shared ignite-arms.json → seat file for live cdp only.
    /// Debug starts empty so sibling cannot ghost-fire live arms.
    /// </summary>
    void TryMigrateLegacyUnlocked()
    {
        if (File.Exists(StorePath)) return;
        if (!string.Equals(Seat, "cdp", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(LegacyStorePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.Copy(LegacyStorePath, StorePath);
            try { File.Move(LegacyStorePath, LegacyStorePath + ".migrated", overwrite: true); }
            catch { /* best-effort */ }
        }
        catch { /* first load without migration */ }
    }

    /// <summary>Merge-on-persist (ADR-0219 §DI.5 two-writer fix): the file may hold arms written by
    /// sibling processes (session/bridge arm the host while the service flight owns the file).
    /// Without the merge, this persist clobbers sibling arms — the wake-postman silent loss of 2026-09-09.</summary>
    void PersistUnlocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var merged = new List<IgniteArm>();
        foreach (var arm in Arms)
            merged.Add(Clone(arm));
        try
        {
            if (File.Exists(StorePath))
            {
                var doc = JsonSerializer.Deserialize<ArmStoreDoc>(File.ReadAllText(StorePath), JsonOpts);
                if (doc?.Arms is { Count: > 0 })
                    foreach (var arm in doc.Arms)
                        if (!merged.Any(m => m.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase)))
                            merged.Add(Clone(arm));
            }
        }
        catch { /* best-effort — corrupt file loses siblings, not this persist */ }

        var outDoc = new ArmStoreDoc
        {
            Schema = StoreSchema,
            SavedUtc = _time.GetUtcNow(),
            Arms = merged
        };
        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(outDoc, JsonOpts));
        File.Move(tmp, StorePath, overwrite: true);
    }

    /// <summary>File is SSOT across processes: adopt arms written by siblings, drop in-memory
    /// arms the file no longer has (sibling fired/disarmed them) — in-flight fires are kept.</summary>
    internal void ReloadFromFileUnlocked()
    {
        if (!File.Exists(StorePath)) return;
        List<IgniteArm>? fromDisk;
        try
        {
            var doc = JsonSerializer.Deserialize<ArmStoreDoc>(File.ReadAllText(StorePath), JsonOpts);
            fromDisk = doc?.Arms ?? [];
        }
        catch { return; }

        foreach (var arm in fromDisk)
            if (!Arms.Any(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase)))
                Arms.Add(Clone(arm));

        var firingIds = Firing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Arms.RemoveAll(a =>
            !firingIds.Contains(a.Id)
            && !fromDisk.Any(d => d.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)));
    }
}
