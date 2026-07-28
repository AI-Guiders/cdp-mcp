#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    static object Slim(IgniteArm a) => new
    {
        id = a.Id,
        @event = a.Event,
        status = a.Status,
        task = a.Task,
        charge_mode = a.ChargeMode,
        message = a.Message.Length > 160 ? a.Message[..160] + "…" : a.Message,
        chat = a.Chat,
        port = a.Port,
        once = a.Once,
        last_once = a.LastOnce,
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
        ChargeMode = a.ChargeMode,
        Task = a.Task,
        Chat = a.Chat,
        Port = a.Port,
        Once = a.Once,
        LastOnce = a.LastOnce,
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

    static string? TryGetDetail(object? result)
    {
        if (result is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("detail", out var d))
                return d.GetString();
            if (doc.RootElement.TryGetProperty("phase", out var p))
                return p.GetString();
        }
        catch { /* ignore */ }

        return null;
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
        /// <summary>minimal (default): fire canonical wake charge; custom/expand/legacy: stored message templates (discouraged).</summary>
        public string ChargeMode { get; set; } = "minimal";
        public string? Task { get; set; }
        public string? Chat { get; set; }
        public int Port { get; set; } = IdeIgniteChannel.DefaultPort;
        public bool Once { get; set; } = true;
        /// <summary>Await-operator latch: after successful fire → status=awaiting; block repeat last_once arms.</summary>
        public bool LastOnce { get; set; }
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
