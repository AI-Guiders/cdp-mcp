#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// First auto/hybrid criteria producer: stage-scoped change plan with anchors
/// feeds DoR "Blast radius…" evidence_ref (change_plan:{id}).
/// TM: change_plan seed|anchor|check|ack|scene.
/// </summary>
internal static partial class IdeChangePlanner
{
    public const string Schema = "change_plan/v0";
    public const string BlastRadiusDor = "Blast radius of change is understood";
    public const string EvidencePrefix = "change_plan:";

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
}
