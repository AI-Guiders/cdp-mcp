#nullable enable
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// AutoIgnition ARM — IDE-owned schedule. Agent arms; harness waits (timer/event) and CDT-injects.
/// Persist: %LocalAppData%/cdp-mcp/ignite-arms.json. No shell loops.
/// </summary>
internal static class IdeIgniteArmHost
{
    public const string StoreSchema = "ignite_arms/v1";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    static readonly object Gate = new();
    static readonly ConcurrentDictionary<string, byte> Firing = new(StringComparer.Ordinal);
    static List<IgniteArm> Arms = [];
    static bool Loaded;
    static int HostStarted;
    static CancellationTokenSource? HostCts;

    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        "ignite-arms.json");

    public static void EnsureStarted()
    {
        EnsureLoaded();
        if (Interlocked.Exchange(ref HostStarted, 1) == 1) return;
        HostCts = new CancellationTokenSource();
        _ = Task.Run(() => TimerLoopAsync(HostCts.Token));
    }

    public static IReadOnlyList<IgniteArm> Snapshot()
    {
        EnsureLoaded();
        lock (Gate) return Arms.Select(Clone).ToList();
    }

    public static object SceneSlice()
    {
        var list = Snapshot().Where(a => a.Status is "armed" or "firing" or "error").ToList();
        return new
        {
            count = list.Count,
            armed = list.Select(Slim).ToList()
        };
    }

    public static object Arm(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureStarted();
        var message = Opt(args, "message") ?? Opt(args, "text") ?? Opt(args, "msg") ?? Opt(args, "prompt");
        var task = Opt(args, "task") ?? Opt(args, "next") ?? Opt(args, "label");
        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(task))
            return Err("arm", "message_or_task_required", "arm message=… and/or task=…");

        var when = NormalizeEvent(Opt(args, "when") ?? Opt(args, "event") ?? Opt(args, "on") ?? "timer");
        var inRaw = Opt(args, "in") ?? Opt(args, "after") ?? Opt(args, "delay");
        var port = OptInt(args, "port") ?? IdeIgniteChannel.DefaultPort;
        var chat = Opt(args, "chat") ?? Opt(args, "title");
        var once = OptBool(args, "once") ?? true;
        var okOnly = OptBool(args, "ok_only") ?? true;
        var settle = OptInt(args, "settle_seconds") ?? (when is "timer" or "manual" ? 2 : 8);
        var wait = OptInt(args, "wait_seconds") ?? 90;
        var id = Opt(args, "id");
        if (string.IsNullOrWhiteSpace(id))
            id = "arm-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                 Guid.NewGuid().ToString("N")[..6];

        DateTimeOffset? due = null;
        if (when == "timer")
        {
            if (!TryParseDue(inRaw, Opt(args, "at"), out due, out var perr))
                return Err("arm", "bad_timer", perr);
        }
        else if (!string.IsNullOrWhiteSpace(inRaw) && TryParseDuration(inRaw!, out var d))
        {
            // optional delay before first eligibility for event arms
            due = DateTimeOffset.UtcNow + d;
        }

        if (string.IsNullOrWhiteSpace(message))
            message = "[autoignite/{event}] Next: {task}";

        var arm = new IgniteArm
        {
            Id = id!,
            Event = when,
            Message = message!,
            Task = task,
            Chat = chat,
            Port = port,
            Once = once,
            OkOnly = okOnly,
            SettleSeconds = Math.Clamp(settle, 0, 120),
            WaitSeconds = Math.Clamp(wait, 5, 600),
            DueUtc = due,
            InRaw = inRaw,
            Status = "armed",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        lock (Gate)
        {
            Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            Arms.Add(arm);
            PersistUnlocked();
        }

        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "arm",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = $"ignite · armed · {when}" + (due is { } d0 ? $" · due {d0:HH:mm:ss}Z" : ""),
            arm = Slim(arm),
            arms = SceneSlice(),
            hint = when == "timer"
                ? "Harness fires when due — end your turn; no shell watcher."
                : $"Harness fires on {when} (ok_only={okOnly}). Kick cdp_build/cdp_test then end turn."
        };
    }

    public static object Disarm(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        var id = Opt(args, "id") ?? Opt(args, "arm");
        var all = OptBool(args, "all") == true
                  || string.Equals(Opt(args, "when") ?? Opt(args, "event"), "all", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(id, "all", StringComparison.OrdinalIgnoreCase);

        int removed;
        lock (Gate)
        {
            if (all)
            {
                removed = Arms.Count;
                Arms.Clear();
            }
            else if (!string.IsNullOrWhiteSpace(id))
            {
                removed = Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                return Err("disarm", "id_required", "disarm id=… or all=true");
            }

            PersistUnlocked();
        }

        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "disarm",
            pulse = $"ignite · disarmed · {removed}",
            removed,
            arms = SceneSlice()
        };
    }

    public static object List()
    {
        EnsureLoaded();
        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "list",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = $"ignite · arms · {list.Count(a => a.Status == "armed")}/{list.Count}",
            arms = list.Select(Slim).ToList(),
            store = StorePath,
            hint = "op=arm when=build_finished|test_finished|timer in=5m message=… [task=…]"
        };
    }

    /// <summary>Lifecycle hooks — call after build/test complete. Non-blocking fire.</summary>
    public static void Notify(string eventName, bool ok, string? pulse = null, string? detail = null)
    {
        EnsureStarted();
        var ev = NormalizeEvent(eventName);
        List<IgniteArm> hits;
        lock (Gate)
        {
            hits = Arms.Where(a =>
                    a.Status == "armed"
                    && a.Event.Equals(ev, StringComparison.OrdinalIgnoreCase)
                    && (!a.OkOnly || ok)
                    && (a.DueUtc is null || a.DueUtc <= DateTimeOffset.UtcNow))
                .Select(Clone)
                .ToList();
        }

        foreach (var arm in hits)
            QueueFire(arm, ok, pulse, detail);
    }

    static void QueueFire(IgniteArm arm, bool ok, string? pulse, string? detail)
    {
        if (!Firing.TryAdd(arm.Id, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                SetStatus(arm.Id, "firing", null);
                if (arm.SettleSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(arm.SettleSeconds)).ConfigureAwait(false);

                var msg = Expand(arm.Message, arm, ok, pulse, detail);
                var result = await IdeIgniteChannel.FireAsync(
                    arm.Port, msg, arm.Chat, arm.WaitSeconds, CancellationToken.None).ConfigureAwait(false);

                var firedOk = result is { } && TryGetOk(result);
                if (firedOk)
                {
                    if (arm.Once)
                        Remove(arm.Id);
                    else
                        SetStatus(arm.Id, "armed", null, fired: DateTimeOffset.UtcNow);
                }
                else
                {
                    var err = TryGetError(result) ?? "fire_failed";
                    SetStatus(arm.Id, "error", err);
                }
            }
            catch (Exception ex)
            {
                SetStatus(arm.Id, "error", ex.Message);
            }
            finally
            {
                Firing.TryRemove(arm.Id, out _);
            }
        });
    }

    static async Task TimerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            List<IgniteArm> due;
            lock (Gate)
            {
                var now = DateTimeOffset.UtcNow;
                due = Arms.Where(a =>
                        a.Status == "armed"
                        && a.Event == "timer"
                        && a.DueUtc is { } d
                        && d <= now)
                    .Select(Clone)
                    .ToList();
            }

            foreach (var arm in due)
                QueueFire(arm, ok: true, pulse: "timer", detail: arm.InRaw);
        }
    }

    static void SetStatus(string id, string status, string? error, DateTimeOffset? fired = null)
    {
        lock (Gate)
        {
            var a = Arms.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (a is null) return;
            a.Status = status;
            a.LastError = error;
            if (fired is { } f) a.FiredUtc = f;
            if (status == "firing") a.FiredUtc = DateTimeOffset.UtcNow;
            PersistUnlocked();
        }
    }

    static void Remove(string id)
    {
        lock (Gate)
        {
            Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            PersistUnlocked();
        }
    }

    static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (Loaded) return;
            Loaded = true;
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

    static string Expand(string template, IgniteArm arm, bool ok, string? pulse, string? detail)
    {
        var t = template
            .Replace("{event}", arm.Event, StringComparison.OrdinalIgnoreCase)
            .Replace("{task}", arm.Task ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{ok}", ok ? "ok" : "fail", StringComparison.OrdinalIgnoreCase)
            .Replace("{pulse}", pulse ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{detail}", detail ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", arm.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{when}", DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        return t;
    }

    public static string NormalizeEvent(string? raw)
    {
        var e = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return e switch
        {
            "build" or "build_done" or "build_ok" or "build_finished" or "on_build" => "build_finished",
            "test" or "tests" or "test_done" or "test_finished" or "on_test" => "test_finished",
            "time" or "delay" or "sleep" or "timer" or "in" => "timer",
            "manual" or "now" or "fire" => "manual",
            _ when e.Length == 0 => "timer",
            _ => e
        };
    }

    public static bool TryParseDuration(string raw, out TimeSpan span)
    {
        span = default;
        var s = raw.Trim().ToLowerInvariant();
        var m = Regex.Match(s, @"^(\d+)\s*(ms|s|m|h|d|sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours|day|days)?$");
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var n) || n < 0) return false;
        var u = m.Groups[2].Value;
        span = u switch
        {
            "ms" => TimeSpan.FromMilliseconds(n),
            "" or "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.FromSeconds(n),
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(n),
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(n),
            "d" or "day" or "days" => TimeSpan.FromDays(n),
            _ => TimeSpan.Zero
        };
        return span > TimeSpan.Zero || n == 0;
    }

    static bool TryParseDue(string? inRaw, string? atRaw, out DateTimeOffset? due, out string error)
    {
        due = null;
        error = "";
        if (!string.IsNullOrWhiteSpace(atRaw)
            && DateTimeOffset.TryParse(atRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            due = at;
            return true;
        }

        if (string.IsNullOrWhiteSpace(inRaw))
        {
            error = "timer requires in=30s|5m|2h or at=ISO-8601";
            return false;
        }

        if (!TryParseDuration(inRaw!, out var d))
        {
            error = $"bad duration '{inRaw}' (use 30s|5m|2h)";
            return false;
        }

        due = DateTimeOffset.UtcNow + d;
        return true;
    }

    static object Slim(IgniteArm a) => new
    {
        id = a.Id,
        @event = a.Event,
        status = a.Status,
        task = a.Task,
        message = a.Message.Length > 160 ? a.Message[..160] + "…" : a.Message,
        chat = a.Chat,
        port = a.Port,
        once = a.Once,
        ok_only = a.OkOnly,
        settle_seconds = a.SettleSeconds,
        due_utc = a.DueUtc,
        in_raw = a.InRaw,
        created_utc = a.CreatedUtc,
        fired_utc = a.FiredUtc,
        last_error = a.LastError
    };

    static IgniteArm Clone(IgniteArm a) => new()
    {
        Id = a.Id,
        Event = a.Event,
        Message = a.Message,
        Task = a.Task,
        Chat = a.Chat,
        Port = a.Port,
        Once = a.Once,
        OkOnly = a.OkOnly,
        SettleSeconds = a.SettleSeconds,
        WaitSeconds = a.WaitSeconds,
        DueUtc = a.DueUtc,
        InRaw = a.InRaw,
        Status = a.Status,
        LastError = a.LastError,
        CreatedUtc = a.CreatedUtc,
        FiredUtc = a.FiredUtc
    };

    static object Err(string op, string error, string detail) => new
    {
        schema = IdeIgniteChannel.Schema,
        ok = false,
        op,
        error,
        detail,
        go = IdeIgniteChannel.GoName,
        tool = IdeIgniteChannel.ToolName
    };

    static bool TryGetOk(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch { return false; }
    }

    static string? TryGetError(object? result)
    {
        if (result is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
        return null;
    }

    static bool? OptBool(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False) return el.GetBoolean();
        if (el.ValueKind == JsonValueKind.String
            && bool.TryParse(el.GetString(), out var b)) return b;
        return null;
    }

    internal sealed class IgniteArm
    {
        public string Id { get; set; } = "";
        public string Event { get; set; } = "timer";
        public string Message { get; set; } = "";
        public string? Task { get; set; }
        public string? Chat { get; set; }
        public int Port { get; set; } = IdeIgniteChannel.DefaultPort;
        public bool Once { get; set; } = true;
        public bool OkOnly { get; set; } = true;
        public int SettleSeconds { get; set; } = 8;
        public int WaitSeconds { get; set; } = 90;
        public DateTimeOffset? DueUtc { get; set; }
        public string? InRaw { get; set; }
        public string Status { get; set; } = "armed";
        public string? LastError { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset? FiredUtc { get; set; }
    }

    sealed class ArmStoreDoc
    {
        public string Schema { get; set; } = StoreSchema;
        public DateTimeOffset SavedUtc { get; set; }
        public List<IgniteArm> Arms { get; set; } = [];
    }
}
