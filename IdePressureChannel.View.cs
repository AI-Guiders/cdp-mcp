#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePressureChannel
{
    static string[] ChecklistLines(SessionContext session, PressureDoc? doc)
    {
        var armed = doc?.Armed == true;
        var hasBody = doc?.Body is { Length: > 0 };
        return
        [
            armed ? "* ARMED — L1 window (~2–3 turns)" : "· idle — arm on L1 notify",
            hasBody ? "* stash present" : (armed ? "! stash EMPTY — op=stash body=" : "· no stash"),
            "1 AutoIgnition — re-ARM timer before end turn (go=ignite_desk)",
            "2 Task Manager — feature/task focus in WitDB (go=plan)",
            "3 Habitat = CDP — buffer/cockpit/shell; not Cursor host Write",
            "4 Invariants / decisions / open / next — into stash body",
            $"· session {session.Phase}/{session.Object} · root={(session.ProjectRoot is { Length: > 0 } ? Path.GetFileName(session.ProjectRoot) : "—")}"
        ];
    }

    static object[] SceneNext(bool armed, PressureDoc? doc)
    {
        var list = new List<object>();
        if (!armed)
            list.Add(new { go = GoName, label = "Arm L1", why = "op=arm" });
        else if (doc?.Body is not { Length: > 0 })
            list.Add(new { go = GoName, label = "Stash now", why = "op=stash body=" });
        list.Add(new { go = "ignite_desk", label = "AutoIgnition", why = "op=arm when=timer in=1s" });
        list.Add(new { go = "plan", label = "Task Manager", why = "focus / next task" });
        if (doc?.Body is { Length: > 0 })
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
            explain = IdeExplainability.ToObject(Explain(doc)),
            hint = "Disarmed. Last stash still on disk (op=recall)."
        };
    }

    static object Recall()
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
                explain = IdeExplainability.ToObject(Explain(null)),
                hint = "No stash yet."
            };
        }

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
            explain = IdeExplainability.ToObject(Explain(doc)),
            hint = "Durable stash — use after host summarization; do not trust platform summary alone."
        };
    }
}
