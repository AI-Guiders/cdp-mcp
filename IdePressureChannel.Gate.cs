#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Recall gate under pressure desk (CDP-ADR-0024):
/// pull → reconcile (self-steer) → align → ready.
/// </summary>
internal static partial class IdePressureChannel
{
    public const string GatePull = "pull";
    public const string GateReconcile = "reconcile";
    public const string GateAlign = "align";
    public const string GateReady = "ready";

    static string? NormalizeGate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "pull" or "recall_pull" or "1" => GatePull,
            "reconcile" or "recon" or "steer" or "2" => GateReconcile,
            "align" or "aligned" or "3" => GateAlign,
            "ready" or "ok" or "green" or "4" => GateReady,
            "clear" or "none" or "idle" or "-" => null,
            _ => null
        };
    }

    static string? NextGateOp(string? gate) => gate switch
    {
        GatePull => "cdp_pressure op=reconcile (self-steer Domain/TM/next)",
        GateReconcile => "cdp_pressure op=align (stash+TM persist)",
        GateAlign => "cdp_pressure op=ready",
        GateReady => "exit recall → explore/plan/act; clear when L1 done",
        _ => "cdp_pressure op=recall"
    };

    static object AdvanceGate(SessionContext session, IReadOnlyDictionary<string, JsonElement> args, string target)
    {
        var doc = Load() ?? new PressureDoc();
        var gate = NormalizeGate(target);
        if (gate is null)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                go = GoName,
                tool = ToolName,
                op = "gate",
                error = "unknown_gate",
                hint = "to=pull|reconcile|align|ready (or op=reconcile|align|ready)"
            };
        }

        var note = Opt(args, "note") ?? Opt(args, "body") ?? Opt(args, "why");
        doc.RecallGate = gate;
        doc.RecallGateUtc = DateTime.UtcNow.ToString("o");
        if (note is { Length: > 0 })
            doc.RecallGateNote = note;
        if (session.ProjectRoot is { Length: > 0 })
            doc.ProjectRoot = session.ProjectRoot;
        doc.Phase = CdpEnumParse.ToWire(session.Phase);
        doc.Object = CdpEnumParse.ToWire(session.Object);
        Save(doc);

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = gate,
            pulse = PulseLine(),
            armed = doc.Armed,
            recall_gate = gate,
            recall_gate_note = doc.RecallGateNote,
            has_stash = doc.Body is { Length: > 0 },
            explain = IdeExplainability.ToObject(Explain(doc)),
            next = GateSceneNext(gate),
            hint = gate switch
            {
                GatePull => "Pulled. Reconcile: compare memo vs priority; self-steer when SSOT suffices (internal locus).",
                GateReconcile => "Reconcile marked. Align: persist Domain/TM/stash corrections, then op=ready.",
                GateAlign => "Aligned. Mark ready when corrections are durable, then leave recall into act.",
                GateReady => "Recall gate green — explore/plan/act allowed. Do not invent from stale Domain.",
                _ => "Gate updated."
            }
        };
    }

    static object[] GateSceneNext(string? gate)
    {
        var list = new List<object>();
        switch (gate)
        {
            case GatePull:
                list.Add(new { go = GoName, label = "Reconcile (self-steer)", why = "op=reconcile — decide Domain/TM/next" });
                list.Add(new { go = GoName, label = "Memo line", why = "op=line limit=5" });
                list.Add(new { go = "plan", label = "Task Manager", why = "focus may need invent/park" });
                break;
            case GateReconcile:
                list.Add(new { go = GoName, label = "Align", why = "op=align — stash+TM persist" });
                list.Add(new { go = GoName, label = "Stash", why = "op=stash body=" });
                list.Add(new { go = "plan", label = "Task Manager", why = "confirm focus after steer" });
                break;
            case GateAlign:
                list.Add(new { go = GoName, label = "Ready", why = "op=ready — exit recall" });
                break;
            case GateReady:
                list.Add(new { go = "plan", label = "Task Manager", why = "act on focused feature" });
                list.Add(new { go = GoName, label = "Clear L1", why = "op=clear when compact done" });
                break;
            default:
                list.Add(new { go = GoName, label = "Recall pull", why = "op=recall" });
                break;
        }

        return list.ToArray();
    }
}
