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
        var lines = ChecklistLines(session, doc);
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
            has_stash = doc?.Body is { Length: > 0 },
            view = new { schema = SchemaVersion, lines },
            next = SceneNext(armed, doc),
            hint = armed
                ? "L1 armed — fill stash (body=) then keep flying in CDP; re-ARM ignite before end turn. Do not offer export ritual."
                : "On L1 pressure notify: op=arm → checklist → op=stash body=. Habitat=CDP (buffer/cockpit/ignite), not Cursor Write."
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
            view = new { schema = SchemaVersion, lines = ChecklistLines(session, doc) },
            next = SceneNext(true, doc),
            hint = "Armed. Stash invariants now (AutoIgnition / Task Manager / CDP locus). Slim desk shows pressure pulse until clear."
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
                hint = "stash body= markdown/text: goal, decisions, open, next, ignite ARM?, plan focus, paths"
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

        // Also write human-readable LATEST.md beside JSON for recall without tool.
        var mdPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "pressure-LATEST.md");
        try
        {
            File.WriteAllText(mdPath, RenderMd(doc), Encoding.UTF8);
        }
        catch
        {
            /* best-effort */
        }

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
            chars = doc.Body.Length,
            next = new object[]
            {
                new { go = GoName, label = "Scene", why = "op=scene" },
                new { go = "ignite_desk", label = "Re-ARM ignite", why = "op=arm when=timer in=1s — keep autonomy" },
                new { go = "plan", label = "Task Manager", why = "confirm focus survived" },
                new { go = GoName, label = "Clear when compact done", why = "op=clear" }
            },
            hint = "Stashed durable. Keep work in CDP; re-ARM AutoIgnition before ending turn."
        };
    }
}
