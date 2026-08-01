#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePressureChannel
{
    static string[] ChecklistLines(SessionContext session, PressureDoc? doc, int memoCount = 0)
    {
        var armed = doc?.Armed == true;
        var hasBody = doc?.Body is { Length: > 0 };
        var domainDir = IdeDomainPulse.ResolveDir(session.ProjectRoot ?? doc?.ProjectRoot);
        var domainOk = domainDir is { Length: > 0 } && Directory.Exists(domainDir)
            && Directory.EnumerateFiles(domainDir, "*.md").Any();
        return
        [
            armed ? "* ARMED — L1 window (~2–3 turns)" : "· idle — arm on L1 notify",
            hasBody ? "* stash present" : (armed ? "! stash EMPTY — op=stash body=" : "· no stash"),
            memoCount > 0
                ? $"* memo line · {memoCount} — op=line (anti-compaction)"
                : "! memo line empty — stash or op=memo body=",
            "1 AutoIgnition — re-ARM timer before end turn (go=ignite_desk)",
            "2 Task Manager — feature/task focus in WitDB (go=plan)",
            "3 Habitat = CDP — buffer/cockpit/shell; not Cursor host Write",
            domainOk
                ? "4 Domain — stamp/recall cards (.cdp/domain); dig before ask"
                : "4 Domain — seed .cdp/domain cards; stamp after ship; dig before ask",
            "5 Invariants / decisions / open / next — into stash body (+ memo line)",
            GateChecklistLine(doc),
            $"· session {session.Phase}/{session.Object} · root={(session.ProjectRoot is { Length: > 0 } ? Path.GetFileName(session.ProjectRoot) : "—")}"
        ];
    }

    static string GateChecklistLine(PressureDoc? doc)
    {
        var gate = NormalizeGate(doc?.RecallGate);
        return gate switch
        {
            GatePull => "* recall gate · pull — next op=reconcile (self-steer)",
            GateReconcile => "* recall gate · reconcile — next op=align",
            GateAlign => "* recall gate · align — next op=ready",
            GateReady => "* recall gate · ready — exit to explore/plan/act",
            _ => "· recall gate idle — op=recall → ready when SSOT else pull"
        };
    }

    static object[] SceneNext(bool armed, PressureDoc? doc, int memoCount = 0)
    {
        var list = new List<object>();
        var gate = NormalizeGate(doc?.RecallGate);
        if (gate is { Length: > 0 })
            list.AddRange(GateSceneNext(gate));
        if (!armed)
            list.Add(new { go = GoName, label = "Arm L1", why = "op=arm" });
        else if (doc?.Body is not { Length: > 0 })
            list.Add(new { go = GoName, label = "Stash now", why = "op=stash body=" });
        list.Add(new { go = GoName, label = "Memo line", why = memoCount > 0 ? "op=line limit=5" : "op=memo body=" });
        list.Add(new { go = "ignite_desk", label = "AutoIgnition", why = "op=arm when=timer in=1s" });
        list.Add(new { go = "plan", label = "Task Manager", why = "focus / next task" });
        list.Add(new { go = GoName, label = "Domain axis", why = "stash Domain + .cdp/domain pulse" });
        if (doc?.Body is { Length: > 0 } && gate is null)
            list.Add(new { go = GoName, label = "Recall stash", why = "op=recall" });
        if (armed)
            list.Add(new { go = GoName, label = "Clear", why = "op=clear after compact" });
        return list.ToArray();
    }

    static object Clear()
    {
        var doc = Load() ?? new PressureDoc();
        doc.Armed = false;
        doc.ClearedUtc = DateTime.UtcNow.ToString("o");
        doc.RecallGate = null;
        doc.RecallGateUtc = null;
        doc.RecallGateNote = null;
        // Keep last body for recall until overwritten.
        Save(doc);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "clear",
            pulse = PulseLine(),
            armed = false,
            has_stash = doc.Body is { Length: > 0 },
            memo_count = CountMemos(),
            explain = IdeExplainability.ToObject(Explain(doc)),
            hint = "Disarmed. Last stash + memo line still on disk (op=recall / op=line)."
        };
    }

static object Recall(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var doc = Load();
        if (doc is null)
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                go = GoName,
                tool = ToolName,
                op = "recall",
                pulse = "pressure · idle",
                empty = true,
                recall_gate = (string?)null,
                memo_count = CountMemos(),
                explain = IdeExplainability.ToObject(Explain(null)),
                hint = "No hot stash — try op=line for memo history."
            };
        }

        var strict = BoolOr(args, "strict", defaultValue: false);
        var autoReady = !strict && SsotSufficient(doc);
        var gate = autoReady ? GateReady : GatePull;
        doc.RecallGate = gate;
        doc.RecallGateUtc = DateTime.UtcNow.ToString("o");
        if (autoReady)
            doc.RecallGateNote = "ssot_auto";
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
            op = "recall",
            pulse = PulseLine(),
            armed = doc.Armed,
            stash_path = FilePath,
            body = doc.Body,
            why = doc.Why,
            project_root = doc.ProjectRoot,
            phase = doc.Phase,
            ignite = doc.IgniteNote,
            plan = doc.PlanNote,
            armed_utc = doc.ArmedUtc,
            stash_utc = doc.StashUtc,
            recall_gate = gate,
            ssot_auto = autoReady,
            memo_count = CountMemos(),
            explain = IdeExplainability.ToObject(Explain(doc)),
            next = GateSceneNext(gate),
            hint = autoReady
                ? "SSOT already sufficient — gate auto-ready. Exit recall → explore/plan/act. strict=true forces pull."
                : "Recall pull — reconcile next: compare memo vs priority; self-steer when SSOT suffices. op=line for history."
        };
    }
}
