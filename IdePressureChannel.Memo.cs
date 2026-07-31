#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Append-only agent memo line — anti-compaction archive beside last-wins stash.
/// Summarize yourself before the host summarizes you. Ops: memo|line.
/// </summary>
internal static partial class IdePressureChannel
{
    public static string MemoPath => Path.Combine(SeatStateDir, "pressure-memo.jsonl");

    public static string MemoLatestMdPath => Path.Combine(SeatStateDir, "pressure-memo-LATEST.md");

    public static string LegacyMemoPath => Path.Combine(CdpProfile.StateRoot, "pressure-memo.jsonl");

    public static string LegacyMemoLatestMdPath => Path.Combine(CdpProfile.StateRoot, "pressure-memo-LATEST.md");

    static readonly JsonSerializerOptions MemoJsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static object Memo(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
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
                op = "memo",
                error = "body_required",
                hint = "memo body= — flight konspekt (axes, decisions, open, next). Not raw transcript."
            };
        }

        var entry = AppendMemo(
            session,
            body.Trim(),
            kind: "memo",
            why: Opt(args, "why"),
            ignite: Opt(args, "ignite"),
            plan: Opt(args, "plan"));

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "memo",
            id = entry.Id,
            memo_path = MemoPath,
            md_path = MemoLatestMdPath,
            chars = entry.Body?.Length ?? 0,
            count = CountMemos(),
            pulse = PulseLine(),
            next = new object[]
            {
                new { go = GoName, label = "Line", why = "op=line limit=5" },
                new { go = GoName, label = "Hot stash", why = "op=stash body= (also appends line)" }
            },
            hint = "Appended to agent memo line. Survives host compaction; recall with op=line."
        };
    }

    static object Line(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptInt(args, "limit") ?? 5;
        if (limit < 1) limit = 1;
        if (limit > 50) limit = 50;

        var entries = LoadMemoTail(limit);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "line",
            memo_path = MemoPath,
            count = entries.Count,
            total = CountMemos(),
            entries,
            pulse = PulseLine(),
            hint = entries.Count == 0
                ? "Memo line empty — op=memo body= or op=stash (auto-appends)."
                : "Agent own history (konspekt). Prefer this over host compaction summary."
        };
    }

    /// <summary>Called from stash / ignite handoff — builds the durable line without a second tool call.</summary>
    internal static MemoEntry AppendMemo(
        SessionContext? session,
        string body,
        string kind,
        string? why = null,
        string? ignite = null,
        string? plan = null)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new MemoEntry
        {
            Id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                 Guid.NewGuid().ToString("N")[..8],
            AtUtc = now.ToString("o", CultureInfo.InvariantCulture),
            Kind = kind,
            Body = body,
            Why = why,
            Ignite = ignite,
            Plan = plan,
            ProjectRoot = session?.ProjectRoot,
            Phase = session?.Phase.ToString(),
            Object = session?.Object.ToString()
        };

        lock (Gate)
        {
            // Dedup: identical body as last entry → keep line lean.
            var last = LoadMemoTailUnlocked(1);
            if (last.Count == 1 &&
                string.Equals(last[0].Body, body, StringComparison.Ordinal))
            {
                return last[0];
            }

            TryMigrateLegacyPressureFiles();
            Directory.CreateDirectory(Path.GetDirectoryName(MemoPath)!);
            var line = JsonSerializer.Serialize(entry, MemoJsonOpts);
            File.AppendAllText(MemoPath, line + Environment.NewLine, Encoding.UTF8);

            try
            {
                File.WriteAllText(MemoLatestMdPath, RenderMemoMd(entry), Encoding.UTF8);
            }
            catch
            {
                /* best-effort */
            }
        }

        return entry;
    }

    static int CountMemos()
    {
        lock (Gate)
        {
            if (!File.Exists(MemoPath))
                return 0;
            var n = 0;
            foreach (var _ in File.ReadLines(MemoPath))
            {
                if (_.Length > 0)
                    n++;
            }

            return n;
        }
    }

    static List<MemoEntry> LoadMemoTail(int limit)
    {
        lock (Gate)
            return LoadMemoTailUnlocked(limit);
    }

    static List<MemoEntry> LoadMemoTailUnlocked(int limit)
    {
        if (!File.Exists(MemoPath) || limit < 1)
            return [];

        // Read all then take last N — memo line stays small by design (konspekt).
        var all = new List<MemoEntry>();
        foreach (var line in File.ReadLines(MemoPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var e = JsonSerializer.Deserialize<MemoEntry>(line, MemoJsonOpts);
                if (e is not null)
                    all.Add(e);
            }
            catch
            {
                /* skip corrupt */
            }
        }

        if (all.Count <= limit)
            return all;
        return all.GetRange(all.Count - limit, limit);
    }

    static string RenderMemoMd(MemoEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Pressure memo (anti-compaction)");
        sb.AppendLine();
        sb.AppendLine($"- id: {e.Id}");
        sb.AppendLine($"- at_utc: {e.AtUtc}");
        sb.AppendLine($"- kind: {e.Kind}");
        sb.AppendLine($"- why: {e.Why}");
        sb.AppendLine($"- project_root: {e.ProjectRoot}");
        sb.AppendLine($"- phase: {e.Phase}/{e.Object}");
        sb.AppendLine($"- ignite: {e.Ignite}");
        sb.AppendLine($"- plan: {e.Plan}");
        sb.AppendLine();
        sb.AppendLine("## Body");
        sb.AppendLine();
        sb.AppendLine(e.Body ?? "");
        return sb.ToString();
    }

    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => null
        };
    }

    internal sealed class MemoEntry
    {
        public string Id { get; set; } = "";
        public string? AtUtc { get; set; }
        public string? Kind { get; set; }
        public string? Body { get; set; }
        public string? Why { get; set; }
        public string? Ignite { get; set; }
        public string? Plan { get; set; }
        public string? ProjectRoot { get; set; }
        public string? Phase { get; set; }
        public string? Object { get; set; }
    }
}
