#nullable enable
using static CdpMcp.IdeIgniteArmHost;
using System.Text.Json;

namespace CdpMcp;

internal sealed partial class CdpIgniteArmHost
{
    object Slim(IgniteArm a) => new
    {
        id = a.Id,
        @event = a.Event,
        status = a.Status,
        task = a.Task,
        reason = a.Reason,
        charge_mode = a.ChargeMode,
        message = a.Message.Length > 160 ? a.Message[..160] + "…" : a.Message,
        chat = a.Chat,
        conversation_id = a.ConversationId,
        harness = a.Harness,
        opencode_session = a.OpencodeSession,
        port = a.Port,
        once = a.Once,
        last_once = a.LastOnce,
        ok_only = a.OkOnly,
        settle_seconds = a.SettleSeconds,
        due_utc = a.DueUtc,
        in_raw = a.InRaw,
        created_utc = a.CreatedUtc,
        fired_utc = a.FiredUtc,
        last_error = a.LastError,
        send_invoked_utc = a.SendInvokedUtc,
        send_ok = a.SendOk,
        send_error = a.SendError,
        transcript_observed_utc = a.TranscriptObservedUtc,
        transcript_path = a.TranscriptPath,
        delivery_verdict = Verdict(a)
    };

    IgniteArm Clone(IgniteArm a) => new()
    {
        Id = a.Id,
        Event = a.Event,
        Message = a.Message,
        ChargeMode = a.ChargeMode,
        Task = a.Task,
        Reason = a.Reason,
        Chat = a.Chat,
        ConversationId = a.ConversationId,
        Port = a.Port,
        Once = a.Once,
        LastOnce = a.LastOnce,
        OkOnly = a.OkOnly,
        SettleSeconds = a.SettleSeconds,
        WaitSeconds = a.WaitSeconds,
        DueUtc = a.DueUtc,
        InRaw = a.InRaw,
        TenantWire = a.TenantWire,
        Harness = a.Harness,
        OpencodeSession = a.OpencodeSession,
        Status = a.Status,
        LastError = a.LastError,
        CreatedUtc = a.CreatedUtc,
        FiredUtc = a.FiredUtc,
        SendInvokedUtc = a.SendInvokedUtc,
        SendOk = a.SendOk,
        SendError = a.SendError,
        TranscriptObservedUtc = a.TranscriptObservedUtc,
        TranscriptPath = a.TranscriptPath
    };

    object Err(string op, string error, string detail) => new
    {
        schema = IdeIgniteChannel.Schema,
        ok = false,
        op,
        error,
        detail,
        go = IdeIgniteChannel.GoName,
        tool = IdeIgniteChannel.ToolName
    };

    bool TryGetOk(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch { return false; }
    }

    string? TryGetError(object? result)
    {
        if (result is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    string? TryGetDetail(object? result)
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

    string? TryGetStringProp(object? result, string name)
    {
        if (result is null || string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            return doc.RootElement.TryGetProperty(name, out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    internal string? TryArmId(object? slim)
    {
        if (slim is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(slim));
            if (doc.RootElement.TryGetProperty("id", out var id))
                return id.GetString();
        }
        catch { /* ignore */ }

        return null;
    }

    string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n)) return n;
        return null;
    }

    bool? OptBool(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el)) return null;
        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False) return el.GetBoolean();
        if (el.ValueKind == JsonValueKind.String
            && bool.TryParse(el.GetString(), out var b)) return b;
        return null;
    }


    sealed class ArmStoreDoc
    {
        public string Schema { get; set; } = StoreSchema;
        public DateTimeOffset SavedUtc { get; set; }
        public List<IgniteArm> Arms { get; set; } = [];
    }
}
