#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskCriteriaList(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveStageTarget(store, state, args)
                 ?? throw new ArgumentException("criteria needs active task or title — focus X | criteria");
        var kind = Opt(args, "kind") ?? OptGoArg(args, "kind");
        return store.StageCriterionList(state, id, kind);
    }

    static object TaskCriterionAdd(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveStageTarget(store, state, args)
                 ?? throw new ArgumentException("criterion needs active task — focus X first");
        var kind = Opt(args, "kind") ?? OptGoArg(args, "kind")
                   ?? throw new ArgumentException("criterion needs kind — criterion dor|ac|dod <text>");
        var text = Opt(args, "text") ?? OptGoArg(args, "text") ?? Title(args);
        var mode = Opt(args, "mode") ?? OptGoArg(args, "mode");
        var evidence = Opt(args, "evidence_ref") ?? OptGoArg(args, "evidence_ref")
                       ?? Opt(args, "evidence") ?? OptGoArg(args, "evidence");
        return store.StageCriterionAdd(state, id, kind, text, mode, evidence);
    }

    static object TaskCriterionSetStatus(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var cid = GuidArg(args, "criterion_id") ?? GuidArgGo(args, "criterion_id")
                  ?? GuidArg(args, "id") ?? GuidArgGo(args, "id")
                  ?? throw new ArgumentException("criterion status needs criterion_id");
        var status = Opt(args, "status") ?? OptGoArg(args, "status")
                     ?? throw new ArgumentException("criterion status needs status=pending|met|unmet|waived");
        var evidence = Opt(args, "evidence_ref") ?? OptGoArg(args, "evidence_ref")
                       ?? Opt(args, "evidence") ?? OptGoArg(args, "evidence");
        return store.StageCriterionSetStatus(state, cid, status, evidence);
    }

    static object TaskCriterionDrop(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var cid = GuidArg(args, "criterion_id") ?? GuidArgGo(args, "criterion_id")
                  ?? GuidArg(args, "id") ?? GuidArgGo(args, "id")
                  ?? throw new ArgumentException("criterion drop needs criterion_id");
        return store.StageCriterionDrop(state, cid);
    }

    /// <summary>
    /// Smart criterion verb: list | add | set-status | drop based on args.
    /// </summary>
    static object TaskCriterionSmart(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var action = (Opt(args, "action") ?? OptGoArg(args, "action") ?? "").Trim().ToLowerInvariant();
        if (action is "list" or "criteria")
            return TaskCriteriaList(store, state, args);
        if (action is "drop" or "rm" or "delete")
            return TaskCriterionDrop(store, state, args);
        if (action is "met" or "unmet" or "waived" or "pending" or "status")
        {
            var status = action is "status"
                ? (Opt(args, "status") ?? OptGoArg(args, "status") ?? "")
                : action == "waived" ? "waived" : action;
            var merged = new Dictionary<string, JsonElement>(args, StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = JsonSerializer.SerializeToElement(status)
            };
            return TaskCriterionSetStatus(store, state, merged);
        }

        if ((Opt(args, "kind") ?? OptGoArg(args, "kind")) is { Length: > 0 })
            return TaskCriterionAdd(store, state, args);

        return TaskCriteriaList(store, state, args);
    }
}
