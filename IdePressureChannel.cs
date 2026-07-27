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
/// </summary>
internal static class IdePressureChannel
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
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
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
            hint = "Durable stash — use after host summarization; do not trust platform summary alone."
        };
    }

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

    static string RenderMd(PressureDoc doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Pressure stash (pre-compact)");
        sb.AppendLine();
        sb.AppendLine($"- armed: {doc.Armed}");
        sb.AppendLine($"- armed_utc: {doc.ArmedUtc}");
        sb.AppendLine($"- stash_utc: {doc.StashUtc}");
        sb.AppendLine($"- why: {doc.Why}");
        sb.AppendLine($"- project_root: {doc.ProjectRoot}");
        sb.AppendLine($"- phase: {doc.Phase}/{doc.Object}");
        sb.AppendLine($"- ignite: {doc.IgniteNote}");
        sb.AppendLine($"- plan: {doc.PlanNote}");
        sb.AppendLine();
        sb.AppendLine("## Body");
        sb.AppendLine();
        sb.AppendLine(doc.Body ?? "");
        return sb.ToString();
    }

    static PressureDoc? Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                return JsonSerializer.Deserialize<PressureDoc>(File.ReadAllText(FilePath), JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }

    static void Save(PressureDoc doc)
    {
        lock (Gate)
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts), Encoding.UTF8);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    sealed class PressureDoc
    {
        public string Schema { get; set; } = SchemaVersion;
        public bool Armed { get; set; }
        public string? ArmedUtc { get; set; }
        public string? StashUtc { get; set; }
        public string? ClearedUtc { get; set; }
        public string? Why { get; set; }
        public string? Body { get; set; }
        public string? ProjectRoot { get; set; }
        public string? Phase { get; set; }
        public string? Object { get; set; }
        public string? IgniteNote { get; set; }
        public string? PlanNote { get; set; }
    }
}
