#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePressureChannel
{
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
        sb.AppendLine($"- recall_gate: {doc.RecallGate}");
        sb.AppendLine($"- recall_gate_utc: {doc.RecallGateUtc}");
        sb.AppendLine();
        if (doc.Wave is { Count: > 0 })
        {
            sb.AppendLine("## wave");
            sb.AppendLine();
            foreach (var item in doc.Wave)
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

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
                TryMigrateLegacyPressureFiles();
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

    /// <summary>Hot L1 stash body present — used by ignite wake tier (auto-full when empty).</summary>
    internal static bool HasHotStashBody()
    {
        var doc = Load();
        return doc?.Body is { Length: > 0 };
    }

    /// <summary>
    /// One-shot: flat StateRoot pressure files → seat subdir (cdp / cdp-debug / other).
    /// </summary>
    static void TryMigrateLegacyPressureFiles()
    {
        try
        {
            Directory.CreateDirectory(SeatStateDir);
            MigrateOne(LegacyFilePath, FilePath);
            MigrateOne(LegacyMemoPath, MemoPath);
            MigrateOne(LegacyMemoLatestMdPath, MemoLatestMdPath);
            var legacyMd = Path.Combine(CdpProfile.StateRoot, "pressure-LATEST.md");
            var seatMd = Path.Combine(SeatStateDir, "pressure-LATEST.md");
            MigrateOne(legacyMd, seatMd);
        }
        catch
        {
            /* best-effort */
        }
    }

    static void MigrateOne(string from, string to)
    {
        if (!File.Exists(from) || File.Exists(to))
            return;
        File.Copy(from, to, overwrite: false);
    }

    static void Save(PressureDoc doc)
    {
        lock (Gate)
        {
            TryMigrateLegacyPressureFiles();
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts), Encoding.UTF8);
            File.Move(tmp, FilePath, overwrite: true);
        }

        PublishGlass();
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

    /// <summary>Prefer wave= JSON / array arg; else parse ## wave section from body.</summary>
    static List<string>? ResolveWave(IReadOnlyDictionary<string, JsonElement> args, string? body)
    {
        if (args.TryGetValue("wave", out var el))
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                var fromArr = el.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (fromArr.Count > 0)
                    return fromArr;
            }
            else if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } raw)
            {
                var parsed = ParseWaveJsonOrLines(raw);
                if (parsed is { Count: > 0 })
                    return parsed;
            }
        }

        if (body is { Length: > 0 })
            return ParseWaveSectionFromBody(body);
        return null;
    }

    static List<string>? ParseWaveJsonOrLines(string raw)
    {
        var t = raw.Trim();
        if (t.StartsWith('['))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(t);
                if (arr is { Count: > 0 })
                    return arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            }
            catch
            {
                /* fall through to lines */
            }
        }

        return SplitWaveLines(t);
    }

    static List<string>? ParseWaveSectionFromBody(string body)
    {
        var idx = body.IndexOf("## wave", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var rest = body[(idx + "## wave".Length)..];
        var next = rest.IndexOf("\n## ", StringComparison.Ordinal);
        if (next >= 0)
            rest = rest[..next];
        return SplitWaveLines(rest);
    }

    static List<string>? SplitWaveLines(string text)
    {
        var list = new List<string>();
        foreach (var line in text.Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var label = line.Trim().TrimStart('-', '*', '•').Trim();
            if (label.Length == 0 || label.StartsWith('#')) continue;
            if (list.Any(x => x.Equals(label, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(label);
        }

        return list.Count == 0 ? null : list;
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
        /// <summary>Recall gate wire: pull|reconcile|align|ready (CDP-ADR-0024).</summary>
        public string? RecallGate { get; set; }
        public string? RecallGateUtc { get; set; }
        public string? RecallGateNote { get; set; }
        /// <summary>Structured throughput wave labels (JSON array) — stash wave= / ## wave.</summary>
        public List<string>? Wave { get; set; }
    }
}
