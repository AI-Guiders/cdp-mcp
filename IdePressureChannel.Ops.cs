#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePressureChannel
{
    static object Scene(SessionContext session)
    {
        var doc = Load();
        var armed = doc?.Armed == true;
        var memoCount = CountMemos();
        var lines = ChecklistLines(session, doc, memoCount);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "scene",
            pulse = PulseLine(),
            armed,
            stash_path = FilePath,
            memo_path = MemoPath,
            memo_count = memoCount,
            has_stash = doc?.Body is { Length: > 0 },
            recall_gate = NormalizeGate(doc?.RecallGate),
            explain = IdeExplainability.ToObject(Explain(doc)),
            view = new { schema = SchemaVersion, lines },
            next = SceneNext(armed, doc, memoCount),
            hint = armed
                ? "L1 armed — fill stash (body=) then keep flying in CDP; re-ARM ignite before end turn. Do not offer export ritual."
                : "On L1 pressure notify: op=arm → checklist → op=stash body= (also appends memo line). Habitat=CDP, not Cursor Write."
        };
    }

    static object Arm(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var why = Opt(args, "why") ?? Opt(args, "note") ?? "L1 pressure notify";
        var doc = Load() ?? new PressureDoc();
        doc.Schema = SchemaVersion;
        doc.Armed = true;
        doc.ArmedUtc = DateTime.UtcNow.ToString("o");
        doc.Why = why;
        doc.ProjectRoot = session.ProjectRoot;
        doc.Phase = session.Phase.ToString();
        doc.Object = session.Object.ToString();
        Save(doc);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "arm",
            pulse = PulseLine(),
            armed = true,
            why,
            explain = IdeExplainability.ToObject(Explain(doc)),
            view = new { schema = SchemaVersion, lines = ChecklistLines(session, doc, CountMemos()) },
            next = SceneNext(true, doc, CountMemos()),
            hint = "Armed. Stash invariants now (AutoIgnition / Task Manager / CDP / Domain). Slim desk shows pressure pulse until clear."
        };
    }

    static object Stash(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var body = Opt(args, "body") ?? Opt(args, "text") ?? Opt(args, "content");
        if (string.IsNullOrWhiteSpace(body))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                go = GoName,
                tool = ToolName,
                op = "stash",
                error = "body_required",
                hint = "stash body= markdown/text: goal, decisions, open, next, ignite ARM?, plan focus, Domain cards, paths"
            };
        }

        var doc = Load() ?? new PressureDoc();
        doc.Schema = SchemaVersion;
        doc.Armed = true;
        doc.ArmedUtc ??= DateTime.UtcNow.ToString("o");
        doc.StashUtc = DateTime.UtcNow.ToString("o");
        doc.Body = body.Trim();
        doc.ProjectRoot = session.ProjectRoot ?? doc.ProjectRoot;
        doc.Phase = session.Phase.ToString();
        doc.Object = session.Object.ToString();
        doc.IgniteNote = Opt(args, "ignite") ?? doc.IgniteNote;
        doc.PlanNote = Opt(args, "plan") ?? doc.PlanNote;
        Save(doc);
        var mdPath = WriteLatestMd(doc);

        // Anti-compaction: hot stash also appends agent memo line (konspekt archive).
        var memo = AppendMemo(
            session,
            doc.Body!,
            kind: "stash",
            why: doc.Why,
            ignite: doc.IgniteNote,
            plan: doc.PlanNote);

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "stash",
            pulse = PulseLine(),
            armed = true,
            stash_path = FilePath,
            md_path = mdPath,
            memo_id = memo.Id,
            memo_path = MemoPath,
            chars = doc.Body.Length,
            explain = IdeExplainability.ToObject(Explain(doc)),
            next = new object[]
            {
                new { go = GoName, label = "Scene", why = "op=scene" },
                new { go = GoName, label = "Memo line", why = "op=line limit=5" },
                new { go = "ignite_desk", label = "Re-ARM ignite", why = "op=arm when=timer in=1s — keep autonomy" },
                new { go = "plan", label = "Task Manager", why = "confirm focus survived" },
                new { go = GoName, label = "Clear when compact done", why = "op=clear" }
            },
            hint = "Stashed durable + appended memo line. Keep work in CDP; re-ARM AutoIgnition before ending turn."
        };
    }

    /// <summary>Ignite post-fire provider block — durable handoff for new PF (best-effort, no session required).</summary>
    internal static void StashAutoIgnitionHandoff(string body, string? igniteNote, string? planNote)
    {
        var doc = Load() ?? new PressureDoc();
        doc.Schema = SchemaVersion;
        doc.Armed = true;
        doc.ArmedUtc ??= DateTime.UtcNow.ToString("o");
        doc.StashUtc = DateTime.UtcNow.ToString("o");
        doc.Why = "AutoIgnition provider_blocked after fire";
        doc.Body = body.Trim();
        doc.IgniteNote = igniteNote ?? doc.IgniteNote;
        doc.PlanNote = planNote ?? doc.PlanNote;
        Save(doc);
        WriteLatestMd(doc);

        AppendMemo(
            session: null,
            body: doc.Body!,
            kind: "ignite_handoff",
            why: doc.Why,
            ignite: doc.IgniteNote,
            plan: doc.PlanNote);
    }

    static string WriteLatestMd(PressureDoc doc)
    {
        var mdPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "pressure-LATEST.md");
        try
        {
            File.WriteAllText(mdPath, RenderMd(doc), Encoding.UTF8);
        }
        catch
        {
            /* best-effort */
        }

        return mdPath;
    }
}
