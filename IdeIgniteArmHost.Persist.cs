#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    static void EnsureLoaded()
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
    static void TryMigrateLegacyUnlocked()
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

    static void PersistUnlocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var doc = new ArmStoreDoc
        {
            Schema = StoreSchema,
            SavedUtc = DateTimeOffset.UtcNow,
            Arms = Arms.Select(Clone).ToList()
        };
        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, StorePath, overwrite: true);
    }
}
