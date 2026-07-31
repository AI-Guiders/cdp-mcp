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
    }
}
