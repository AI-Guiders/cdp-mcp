#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>Last-seen fingerprints for freshness scan (ETag / Last-Modified / body hash / feed ids).</summary>
internal static class IdeFreshnessCache
{
    public const string FileName = "freshness-cache.json";

    public static string PathOnDisk =>
        System.IO.Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat, FileName);

    public sealed class Entry
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("etag")] public string? Etag { get; set; }
        [JsonPropertyName("last_modified")] public string? LastModified { get; set; }
        [JsonPropertyName("body_hash")] public string? BodyHash { get; set; }
        [JsonPropertyName("feed_latest_id")] public string? FeedLatestId { get; set; }
        [JsonPropertyName("feed_latest_title")] public string? FeedLatestTitle { get; set; }
        [JsonPropertyName("observed_utc")] public string? ObservedUtc { get; set; }
        [JsonPropertyName("alias")] public string? Alias { get; set; }
    }

    public sealed class Store
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "freshness_cache/v1";
        [JsonPropertyName("entries")] public Dictionary<string, Entry> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Store Load()
    {
        try
        {
            var path = PathOnDisk;
            if (!File.Exists(path))
                return new Store();
            var raw = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Store>(raw, JsonOpts) ?? new Store();
        }
        catch
        {
            return new Store();
        }
    }

    public static void Save(Store store)
    {
        var path = PathOnDisk;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(store, JsonOpts));
    }

    public static int ClearAll()
    {
        var store = Load();
        var n = store.Entries.Count;
        store.Entries.Clear();
        Save(store);
        return n;
    }

    public static int ClearKeys(IEnumerable<string> urlsOrKeys)
    {
        var store = Load();
        var removed = 0;
        foreach (var raw in urlsOrKeys)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var key = Key(raw);
            if (store.Entries.Remove(key)) removed++;
        }
        if (removed > 0) Save(store);
        return removed;
    }

    public static string Key(string url) => url.Trim();
}
