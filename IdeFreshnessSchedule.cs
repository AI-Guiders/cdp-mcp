#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>W3: opt-in timer / nightly digest schedule (seat-local). tick runs scan when due.</summary>
internal static class IdeFreshnessSchedule
{
    public const string FileName = "freshness-schedule.json";

    public static string PathOnDisk =>
        Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat, FileName);

    public sealed class Store
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "freshness_schedule/v1";
        [JsonPropertyName("armed")] public bool Armed { get; set; }
        [JsonPropertyName("when")] public string? When { get; set; }
        [JsonPropertyName("in_raw")] public string? InRaw { get; set; }
        [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = [];
        [JsonPropertyName("take")] public int Take { get; set; } = 12;
        [JsonPropertyName("due_utc")] public string? DueUtc { get; set; }
        [JsonPropertyName("last_tick_utc")] public string? LastTickUtc { get; set; }
        [JsonPropertyName("last_changed_count")] public int? LastChangedCount { get; set; }
        [JsonPropertyName("repeat")] public bool Repeat { get; set; } = true;
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
            if (!File.Exists(path)) return new Store();
            return JsonSerializer.Deserialize<Store>(File.ReadAllText(path), JsonOpts) ?? new Store();
        }
        catch
        {
            return new Store();
        }
    }

    public static void Save(Store store)
    {
        var path = PathOnDisk;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(store, JsonOpts));
    }

    public static object Scene()
    {
        var s = Load();
        var due = TryParseDue(s.DueUtc);
        var now = DateTimeOffset.UtcNow;
        return new
        {
            schema = "freshness_schedule/v1",
            ok = true,
            op = "schedule",
            go = IdeFreshnessChannel.GoName,
            tool = IdeFreshnessChannel.ToolName,
            path = PathOnDisk,
            armed = s.Armed,
            when = s.When,
            in_raw = s.InRaw,
            aliases = s.Aliases,
            take = s.Take,
            due_utc = s.DueUtc,
            due = due is not null && now >= due.Value,
            last_tick_utc = s.LastTickUtc,
            last_changed_count = s.LastChangedCount,
            repeat = s.Repeat,
            hint = "op=arm when=nightly|in=12h aliases=avalonia,php · op=tick · op=disarm"
        };
    }

    public static object Arm(string? when, string? inRaw, IEnumerable<string> aliases, int take, bool repeat)
    {
        var list = aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
            list = ["avalonia", "baseline2026", "php"];

        take = Math.Clamp(take, 1, 40);
        when = string.IsNullOrWhiteSpace(when) ? (string.IsNullOrWhiteSpace(inRaw) ? "nightly" : "timer") : when.Trim().ToLowerInvariant();

        DateTimeOffset due;
        if (string.Equals(when, "nightly", StringComparison.OrdinalIgnoreCase))
        {
            due = NextNightlyUtc(DateTimeOffset.UtcNow);
            inRaw = null;
        }
        else if (!string.IsNullOrWhiteSpace(inRaw) && IdeIgniteArmHost.TryParseDuration(inRaw!, out var span))
        {
            due = DateTimeOffset.UtcNow + span;
            when = "timer";
        }
        else if (IdeIgniteArmHost.TryParseDuration(when!, out var spanWhen))
        {
            due = DateTimeOffset.UtcNow + spanWhen;
            inRaw = when;
            when = "timer";
        }
        else
        {
            return new
            {
                schema = "freshness_schedule/v1",
                ok = false,
                error = "when_or_in_required",
                hint = "when=nightly | in=12h | when=6h"
            };
        }

        var store = new Store
        {
            Armed = true,
            When = when,
            InRaw = inRaw,
            Aliases = list,
            Take = take,
            DueUtc = due.ToString("O"),
            Repeat = repeat,
            LastTickUtc = Load().LastTickUtc,
            LastChangedCount = Load().LastChangedCount
        };
        Save(store);
        return new
        {
            schema = "freshness_schedule/v1",
            ok = true,
            op = "arm",
            go = IdeFreshnessChannel.GoName,
            tool = IdeFreshnessChannel.ToolName,
            armed = true,
            when = store.When,
            in_raw = store.InRaw,
            aliases = store.Aliases,
            take = store.Take,
            due_utc = store.DueUtc,
            repeat = store.Repeat,
            path = PathOnDisk,
            hint = "Autoi/agent: when due → cdp_freshness op=tick. Digest ≠ Проверено."
        };
    }

    public static object Disarm()
    {
        var s = Load();
        s.Armed = false;
        s.DueUtc = null;
        Save(s);
        return new
        {
            schema = "freshness_schedule/v1",
            ok = true,
            op = "disarm",
            armed = false,
            path = PathOnDisk,
            hint = "schedule disarmed"
        };
    }

    public static bool IsDue(Store? s = null)
    {
        s ??= Load();
        if (!s.Armed) return false;
        var due = TryParseDue(s.DueUtc);
        return due is not null && DateTimeOffset.UtcNow >= due.Value;
    }

    public static void MarkTick(Store s, int changedCount, DateTimeOffset now)
    {
        s.LastTickUtc = now.ToString("O");
        s.LastChangedCount = changedCount;
        if (!s.Repeat)
        {
            s.Armed = false;
            s.DueUtc = null;
        }
        else if (string.Equals(s.When, "nightly", StringComparison.OrdinalIgnoreCase))
        {
            s.DueUtc = NextNightlyUtc(now).ToString("O");
        }
        else if (!string.IsNullOrWhiteSpace(s.InRaw) && IdeIgniteArmHost.TryParseDuration(s.InRaw!, out var span))
        {
            s.DueUtc = (now + span).ToString("O");
        }
        else
        {
            s.DueUtc = NextNightlyUtc(now).ToString("O");
            s.When = "nightly";
        }

        Save(s);
    }

    static DateTimeOffset NextNightlyUtc(DateTimeOffset now)
    {
        // 02:00 UTC next occurrence
        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, 2, 0, 0, TimeSpan.Zero);
        if (candidate <= now) candidate = candidate.AddDays(1);
        return candidate;
    }

    static DateTimeOffset? TryParseDue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, out var d) ? d : null;
    }
}
