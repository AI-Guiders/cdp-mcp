#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// First auto/hybrid criteria producer: stage-scoped change plan with anchors
/// feeds DoR "Blast radius…" evidence_ref (change_plan:{id}).
/// TM: change_plan seed|anchor|check|ack|scene.
/// </summary>
internal static class IdeChangePlanner
{
    public const string Schema = "change_plan/v0";
    public const string BlastRadiusDor = "Blast radius of change is understood";
    public const string EvidencePrefix = "change_plan:";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static object Handle(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = (Opt(args, "cp_op") ?? OptGo(args, "cp_op")
                  ?? Opt(args, "action") ?? OptGo(args, "action")
                  ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "pulse" or "status" or "" => Scene(store, state, projectRoot, args),
            "seed" or "open" or "ensure" => Seed(store, state, projectRoot, args),
            "anchor" or "add_anchor" => Anchor(store, state, projectRoot, args),
            "check" => Check(store, state, projectRoot, args, ackManual: false),
            "ack" or "manual_ack" or "ack_manual" => Check(store, state, projectRoot, args, ackManual: true),
            _ => throw new ArgumentException(
                "change_plan op must be scene|seed|anchor|check|ack")
        };
    }

    static object Seed(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var stageId = ResolveStage(state, args)
                      ?? throw new ArgumentException("change_plan seed needs active task — focus X first");
        var dir = ResolveDir(projectRoot, Opt(args, "dir") ?? OptGo(args, "dir"));
        Directory.CreateDirectory(dir);
        var path = PlanPath(dir, stageId);
        var plan = LoadOrCreate(path, stageId);
        var evidence = EvidencePrefix + plan.PlanId;
        var criterion = store.StageCriterionEnsure(
            state, stageId, "dor", BlastRadiusDor, "hybrid", evidence);
        plan.CriterionId = CriterionId(criterion);
        plan.UpdatedUtc = DateTimeOffset.UtcNow;
        Save(path, plan);
        return Pack("seed", plan, path, criterion, Evaluate(plan));
    }

    static object Anchor(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var stageId = ResolveStage(state, args)
                      ?? throw new ArgumentException("change_plan anchor needs active task");
        var anchor = (Opt(args, "anchor") ?? OptGo(args, "anchor")
                      ?? Opt(args, "text") ?? OptGo(args, "text")
                      ?? Opt(args, "title") ?? OptGo(args, "title") ?? "").Trim();
        if (anchor.Length == 0)
            throw new ArgumentException("change_plan anchor needs text — change_plan anchor [F:…] or path");
        var note = Opt(args, "note") ?? OptGo(args, "note");
        var dir = ResolveDir(projectRoot, Opt(args, "dir") ?? OptGo(args, "dir"));
        var path = PlanPath(dir, stageId);
        if (!File.Exists(path))
            _ = Seed(store, state, projectRoot, args);
        var plan = LoadOrCreate(path, stageId);
        if (!plan.Anchors.Any(a => string.Equals(a.Anchor, anchor, StringComparison.OrdinalIgnoreCase)))
            plan.Anchors.Add(new ChangePlanAnchor { Anchor = Truncate(anchor, 240), Note = TruncateNullable(note, 160) });
        plan.UpdatedUtc = DateTimeOffset.UtcNow;
        Save(path, plan);
        return Check(store, state, projectRoot, args, ackManual: false, planHint: plan, pathHint: path);
    }

    static object Check(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args,
        bool ackManual,
        ChangePlanDoc? planHint = null,
        string? pathHint = null)
    {
        var stageId = ResolveStage(state, args)
                      ?? throw new ArgumentException("change_plan check needs active task");
        var dir = ResolveDir(projectRoot, Opt(args, "dir") ?? OptGo(args, "dir"));
        var path = pathHint ?? PlanPath(dir, stageId);
        if (!File.Exists(path) && planHint is null)
            throw new ArgumentException("no change plan — change_plan seed first");
        var plan = planHint ?? LoadOrCreate(path, stageId);
        if (ackManual)
            plan.ManualAcked = true;
        var evidence = EvidencePrefix + plan.PlanId;
        if (plan.CriterionId is null)
        {
            var ensured = store.StageCriterionEnsure(
                state, stageId, "dor", BlastRadiusDor, "hybrid", evidence);
            plan.CriterionId = CriterionId(ensured);
        }

        var eval = Evaluate(plan);
        object criterion;
        if (plan.CriterionId is { } cid)
        {
            var status = eval.Ready ? "met" : "pending";
            criterion = store.StageCriterionSetStatus(state, cid, status, evidence);
        }
        else
        {
            criterion = store.StageCriterionEnsure(
                state, stageId, "dor", BlastRadiusDor, "hybrid", evidence);
        }

        plan.UpdatedUtc = DateTimeOffset.UtcNow;
        Save(path, plan);
        return Pack(ackManual ? "ack" : "check", plan, path, criterion, eval);
    }

    static object Scene(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var stageId = ResolveStage(state, args);
        if (stageId is null)
        {
            return new
            {
                ok = false,
                schema = Schema,
                op = "scene",
                error = "no_active_task",
                hint = "focus a task, then change_plan seed"
            };
        }

        var dir = ResolveDir(projectRoot, Opt(args, "dir") ?? OptGo(args, "dir"));
        var path = PlanPath(dir, stageId.Value);
        if (!File.Exists(path))
        {
            return new
            {
                ok = true,
                schema = Schema,
                op = "scene",
                stage_id = stageId,
                has_plan = false,
                hint = "change_plan seed — open hybrid DoR + empty plan"
            };
        }

        var plan = LoadOrCreate(path, stageId.Value);
        object? criterion = null;
        if (plan.CriterionId is { } cid)
        {
            try
            {
                var list = store.StageCriterionList(state, stageId.Value, "dor");
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(list));
                if (doc.RootElement.TryGetProperty("criteria", out var arr))
                {
                    foreach (var row in arr.EnumerateArray())
                    {
                        if (row.TryGetProperty("criterion_id", out var idEl)
                            && Guid.TryParse(idEl.GetString() ?? idEl.ToString(), out var gid)
                            && gid == cid)
                        {
                            criterion = JsonSerializer.Deserialize<JsonElement>(row.GetRawText());
                            break;
                        }
                    }
                }
            }
            catch
            {
                /* scene stays ok without criterion row */
            }
        }

        return Pack("scene", plan, path, criterion, Evaluate(plan));
    }

    static object Pack(
        string op,
        ChangePlanDoc plan,
        string path,
        object? criterion,
        Eval eval) => new
    {
        ok = true,
        schema = Schema,
        op,
        plan_id = plan.PlanId,
        stage_id = plan.StageId,
        criterion_id = plan.CriterionId,
        path,
        anchors = plan.Anchors,
        anchor_count = plan.Anchors.Count,
        manual_acked = plan.ManualAcked,
        auto_ok = eval.AutoOk,
        needs_manual = eval.NeedsManual,
        ready = eval.Ready,
        evidence_ref = EvidencePrefix + plan.PlanId,
        criterion,
        pulse = PulseLine(plan, eval),
        hint = eval.Ready
            ? "DoR blast-radius met (anchors + ack)"
            : eval.AutoOk
                ? "auto ok — change_plan ack to confirm scope"
                : "add anchors: change_plan anchor [F:…]"
    };

    static string PulseLine(ChangePlanDoc plan, Eval eval) =>
        eval.Ready
            ? $"change_plan · ready · anchors={plan.Anchors.Count}"
            : eval.AutoOk
                ? $"change_plan · auto_ok · needs ack · anchors={plan.Anchors.Count}"
                : $"change_plan · pending · anchors={plan.Anchors.Count}";

    static Eval Evaluate(ChangePlanDoc plan)
    {
        var autoOk = plan.Anchors.Count >= 1;
        var needsManual = autoOk && !plan.ManualAcked;
        var ready = autoOk && plan.ManualAcked;
        return new Eval(autoOk, needsManual, ready);
    }

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
