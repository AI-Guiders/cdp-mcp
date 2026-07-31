#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=pressure</c> / Meta <c>cdp_pressure</c> — L1 pre-compact prep desk.
/// When Cursor injects pressure notify (~2–3 turns before summarization): arm → checklist → stash.
/// Does NOT auto-offer export ritual to operator. Durable stash survives remount.
/// Memo line (<c>op=memo|line</c>): append-only agent konspekt — anti-compaction beyond last-wins stash.
/// Must-remember axes: AutoIgnition re-ARM, Task Manager focus, CDP habitat, Domain cards.
/// Partials: Persist (load/save/md), Ops (scene/arm/stash), Memo (memo/line), View (checklist/recall/clear).
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

    public static string FilePath => Path.Combine(
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
            "memo" or "append" or "note" => Memo(session, args),
            "line" or "history" or "tail" => Line(args),
            "clear" or "disarm" or "done" => Clear(),
            "recall" or "load" or "peek" => Recall(),
            "reconcile" or "recon" or "steer" => AdvanceGate(session, args, GateReconcile),
            "align" or "aligned" => AdvanceGate(session, args, GateAlign),
            "ready" or "gate_ready" => AdvanceGate(session, args, GateReady),
            "gate" => AdvanceGate(session, args, Opt(args, "to") ?? Opt(args, "status") ?? Opt(args, "gate") ?? ""),
            _ => Scene(session)
        };
    }

    /// <summary>Best-effort project_root from last stash (remount domain pulse).</summary>
    internal static string? TryPeekProjectRoot()
    {
        try
        {
            return Load()?.ProjectRoot;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsArmed()
    {
        var doc = Load();
        return doc is { Armed: true };
    }

    public static bool HasStash()
    {
        var doc = Load();
        return doc?.Body is { Length: > 0 };
    }

    /// <summary>Mirror L1 state to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        var doc = Load();
        var armed = doc is { Armed: true };
        CidePressureLatch.Publish(armed, PulseLine(), doc?.Body is { Length: > 0 });
    }

    public static string PulseLine()
    {
        var doc = Load();
        if (doc is null || !doc.Armed)
            return "pressure · idle";
        var stash = doc.Body is { Length: > 0 } ? " · stashed" : " · need stash";
        var gate = NormalizeGate(doc.RecallGate);
        var gatePart = gate is { Length: > 0 } ? $" · recall·{gate}" : "";
        return $"pressure · ARMED{stash}{gatePart}";
    }

    public static object? PulseCardOrNull()
    {
        var doc = Load();
        if (doc is null || !doc.Armed)
            return null;
        var explain = Explain(doc);
        return new
        {
            schema = SchemaVersion,
            armed = true,
            pulse = PulseLine(),
            has_stash = doc.Body is { Length: > 0 },
            at_utc = doc.ArmedUtc,
            go = GoName,
            explain = IdeExplainability.ToObject(explain)
        };
    }

    public static string? ExplainWhyLine()
    {
        var doc = Load();
        if (doc is null || !doc.Armed)
            return null;
        return Explain(doc).WhyLine;
    }

    static IdeExplainability.ExplainCard Explain(PressureDoc? doc)
    {
        if (doc is null || !doc.Armed)
        {
            return IdeExplainability.New(
                "pressure.continuity",
                "idle",
                "L1 pre-compact desk is idle — arm when host injects pressure notify",
                "cdp_pressure op=arm");
        }

        if (doc.Body is not { Length: > 0 })
        {
            return IdeExplainability.New(
                "pressure.continuity",
                "need_stash",
                "L1 armed but stash empty — durable axes not written yet",
                "cdp_pressure op=stash body=");
        }

        var gate = NormalizeGate(doc.RecallGate);
        if (gate is { Length: > 0 } and not GateReady)
        {
            return IdeExplainability.New(
                "pressure.recall_gate",
                $"recall_{gate}",
                gate switch
                {
                    GatePull => "Recall pull — compare memo vs priority; self-steer when SSOT suffices (internal locus)",
                    GateReconcile => "Recall reconcile — decision in progress; persist Domain/TM then align",
                    GateAlign => "Recall align — corrections should be durable; mark ready to leave recall",
                    _ => "Recall gate in progress"
                },
                NextGateOp(gate));
        }

        if (gate == GateReady)
        {
            return IdeExplainability.New(
                "pressure.recall_gate",
                "recall_ready",
                "Recall gate green — explore/plan/act allowed; clear when L1 done",
                NextGateOp(GateReady));
        }

        return IdeExplainability.New(
            "pressure.continuity",
            "stashed",
            "L1 armed with durable stash — recall after compact; clear when done",
            "cdp_pressure op=recall");
    }
}
