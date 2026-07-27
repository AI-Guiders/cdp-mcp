using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Agent-writable IDE prefs under <see cref="CdpProfile.StateRoot"/>/ide-settings.json.
/// Hot keys apply without remount. Process layer = cdp-mcp.toml (ADR 0190). Scoped by ADR 0199.
/// </summary>
internal static class IdeSettingsStore
{
    public const string Schema = "ide_settings/v1";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly object Gate = new();
    static Dictionary<string, string> User = new(StringComparer.OrdinalIgnoreCase);
    static bool Loaded;
    static string? LoadedFromPath;

    public static string FilePath => Path.Combine(CdpProfile.StateRoot, "ide-settings.json");

    public static void Invalidate()
    {
        lock (Gate)
        {
            Loaded = false;
            LoadedFromPath = null;
            User = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            var path = FilePath;
            if (Loaded && string.Equals(LoadedFromPath, path, StringComparison.OrdinalIgnoreCase))
                return;
            Loaded = true;
            LoadedFromPath = path;
            User = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return;
            try
            {
                var doc = JsonSerializer.Deserialize<IdeSettingsDoc>(File.ReadAllText(path), JsonOpts);
                if (doc?.Values is { Count: > 0 })
                    User = new Dictionary<string, string>(doc.Values, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Corrupt prefs → empty user layer; habitat will report on scene.
            }
        }
    }

    public static IReadOnlyDictionary<string, string> SnapshotUser()
    {
        EnsureLoaded();
        lock (Gate) return new Dictionary<string, string>(User, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryGet(string key, out string value)
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (User.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                value = v;
                return true;
            }
        }

        value = "";
        return false;
    }

    public static string? GetOrNull(string key) => TryGet(key, out var v) ? v : null;

    public static void Set(string key, string value)
    {
        EnsureLoaded();
        lock (Gate)
        {
            User[key] = value;
            PersistUnlocked();
        }
    }

    public static bool Unset(string key)
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (!User.Remove(key)) return false;
            PersistUnlocked();
            return true;
        }
    }

    public static int ClearAll()
    {
        EnsureLoaded();
        lock (Gate)
        {
            var n = User.Count;
            User.Clear();
            PersistUnlocked();
            return n;
        }
    }

    public static int? GetInt(string key, int? fallback = null)
    {
        if (!TryGet(key, out var raw)) return fallback;
        return int.TryParse(raw.Trim(), out var n) ? n : fallback;
    }

    static void PersistUnlocked()
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var doc = new IdeSettingsDoc
        {
            Schema = Schema,
            SavedUtc = DateTime.UtcNow.ToString("O"),
            Values = new Dictionary<string, string>(User, StringComparer.OrdinalIgnoreCase)
        };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, path, overwrite: true);
        LoadedFromPath = path;
    }

    sealed class IdeSettingsDoc
    {
        public string Schema { get; set; } = IdeSettingsStore.Schema;
        public string? SavedUtc { get; set; }
        public Dictionary<string, string>? Values { get; set; }
    }
}
