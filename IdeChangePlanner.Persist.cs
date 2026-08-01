#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>Change-plan disk IO + arg helpers (≤ADX soft-warn peel).</summary>
internal static partial class IdeChangePlanner
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static Guid? ResolveStage(
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var raw = Opt(args, "stage_id") ?? OptGo(args, "stage_id")
                  ?? Opt(args, "task_id") ?? OptGo(args, "task_id");
        if (Guid.TryParse(raw, out var g))
            return g;
        return state.ActiveStageId;
    }

    public static string ResolveDir(string? projectRoot, string? dirOverride)
    {
        if (!string.IsNullOrWhiteSpace(dirOverride))
            return Path.GetFullPath(dirOverride);
        if (!string.IsNullOrWhiteSpace(projectRoot))
            return Path.GetFullPath(Path.Combine(projectRoot, ".cdp", "change-plans"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "change-plans");
    }

    static string PlanPath(string dir, Guid stageId) =>
        Path.Combine(dir, stageId.ToString("N") + ".json");

    static ChangePlanDoc LoadOrCreate(string path, Guid stageId)
    {
        if (File.Exists(path))
        {
            try
            {
                var raw = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<ChangePlanDoc>(raw, JsonOpts);
                if (doc is not null && doc.StageId == stageId)
                {
                    doc.Anchors ??= [];
                    return doc;
                }
            }
            catch
            {
                /* recreate */
            }
        }

        return new ChangePlanDoc
        {
            Schema = Schema,
            PlanId = Guid.NewGuid().ToString("N")[..12],
            StageId = stageId,
            Anchors = [],
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    static void Save(string path, ChangePlanDoc plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(plan, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    static Guid CriterionId(object criterionDto)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(criterionDto));
        return doc.RootElement.GetProperty("criterion_id").GetGuid();
    }

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    static string? TruncateNullable(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var t = s.Trim();
        return t.Length <= max ? t : t[..max];
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    static string? OptGo(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return null;
        if (!ga.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    sealed class ChangePlanDoc
    {
        public string Schema { get; set; } = IdeChangePlanner.Schema;
        public string PlanId { get; set; } = "";
        public Guid StageId { get; set; }
        public Guid? CriterionId { get; set; }
        public List<ChangePlanAnchor> Anchors { get; set; } = [];
        public bool ManualAcked { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }

    sealed class ChangePlanAnchor
    {
        public string Anchor { get; set; } = "";
        public string? Note { get; set; }
    }

    readonly record struct Eval(bool AutoOk, bool NeedsManual, bool Ready);
}
