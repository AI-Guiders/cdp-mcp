#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=pressure</c> / Meta <c>cdp_pressure</c> — L1 pre-compact prep desk.
/// When Cursor injects pressure notify (~2–3 turns before summarization): arm → checklist → stash.
/// Does NOT auto-offer export ritual to operator. Durable stash survives remount.
/// Must-remember axes: AutoIgnition re-ARM, Task Manager focus, CDP (not Cursor Write).
/// Partials: Persist (load/save/md), Ops (scene/arm/stash), View (checklist/recall/clear).
/// </summary>
internal static partial class IdePressureChannel
{
    public const string SchemaVersion = "pressure_channel/v1";
    public const string ToolName = "cdp_pressure";
    public const string GoName = "pressure_desk";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        CdpProfile.StateRoot,
        "pressure-stash.json");

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "arm" or "armed" or "l1" => Arm(session, args),
            "stash" or "write" or "save" => Stash(session, args),
            "clear" or "disarm" or "done" => Clear(),
            "recall" or "load" or "peek" => Recall(),
            _ => Scene(session)
        };
    }

    public static bool IsArmed()
    {
        var doc = Load();
        return doc is { Armed: true };
    }

    public static string PulseLine()
    {
        var doc = Load();
        if (doc is null || !doc.Armed)
            return "pressure · idle";
        var stash = doc.Body is { Length: > 0 } ? " · stashed" : " · need stash";
        return $"pressure · ARMED{stash}";
    }

    public static object? PulseCardOrNull()
    {
        var doc = Load();
        if (doc is null || !doc.Armed)
            return null;
        return new
        {
            schema = SchemaVersion,
            armed = true,
            pulse = PulseLine(),
            has_stash = doc.Body is { Length: > 0 },
            at_utc = doc.ArmedUtc,
            go = GoName
        };
    }
}
