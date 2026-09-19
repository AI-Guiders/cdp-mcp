#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=ground</c> / Meta <c>cdp_ground</c> — same-wake soft recalibration (L5).
/// Detect → name (1 line) → next tool → resume. Soft pulse only; never hard block v1.
/// </summary>
internal static class IdeGroundChannel
{
    public const string SchemaVersion = "ground_channel/v0";
    public const string ToolName = "cdp_ground";
    public const string GoName = "ground";

    public const int BufferMillThreshold = 5;

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "pulse").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" => Scene(),
            "reset" => Reset(),
            "mark_recall" => MarkRecall(),
            _ => Pulse()
        };
    }

    public static string PulseLine() => IdeSameWakeLatch.PulseLine();

    public static bool IsBufferMillHot() => IdeSameWakeLatch.BufferOps >= BufferMillThreshold;

    static object Pulse()
    {
        var card = IdeSameWakeLatch.Detect();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "pulse",
            go = GoName,
            tool = ToolName,
            pulse = card.Pulse,
            pattern = card.Pattern,
            next = card.Next,
            resume_ok = card.ResumeOk,
            counts = new
            {
                buffer_ops = IdeSameWakeLatch.BufferOps,
                recall_done = IdeSameWakeLatch.RecallDone,
                ignore_nudge_spent = IdeSameWakeLatch.IgnoreNudgeSpent
            },
            hint = "Same-wake ground: soft mirror + next tool. Not a lecture; not a hard block."
        };
    }

    static object Scene() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        detail = "pulse",
        pulse = PulseLine(),
        awareness = IdeSameWakeLatch.AwarenessPatterns,
        patterns = new[] { "early_act", "buffer_mill", "ignore_hint", "biped", "ok" },
        hint = "go=ground / cdp_ground op=pulse — detect→name→next→resume. Domain card ground.md."
    };

    static object Reset()
    {
        IdeSameWakeLatch.Reset();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "reset",
            go = GoName,
            tool = ToolName,
            pulse = PulseLine(),
            hint = "Same-wake counters cleared (tests / new wake)."
        };
    }

    static object MarkRecall()
    {
        IdeSameWakeLatch.NoteRecall();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "mark_recall",
            go = GoName,
            tool = ToolName,
            pulse = PulseLine(),
            recall_done = true,
            hint = "Recall stamped for this wake — ignore-hint soft nudge armed off."
        };
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }
}
