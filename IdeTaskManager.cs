#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Agent Task Manager (MLP) — Feature=Intent, Task=Stage tree; sticky focus in WitDB.
/// Survives MCP remount like seats. Desk organ <c>go=plan</c> (aliases: work|tasks|tm|feature|task).
/// Partials: Dispatch (op switch), Board (pulse/tree), Mutations (feature/task ops).
/// </summary>
internal static partial class IdeTaskManager
{
    public const string SchemaVersion = "task_manager/v1.3";

    public static object Handle(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = (Opt(args, "tm_op") ?? OptGoArg(args, "op") ?? Opt(args, "op") ?? "board")
            .Trim().ToLowerInvariant();

        object? mutation;
        try
        {
            mutation = Dispatch(store, state, args, op);
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "task_manager",
                error = ex.Message,
                hint =
                    "cmd=\"feature X\" | task Y | share report | share plan | promote | confirm | reject | drop | done | go=plan"
            };
        }

        var board = BuildBoard(store, state, Opt(args, "session_phase") ?? OptGoArg(args, "session_phase"));
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "task_manager",
            go = "plan",
            detail = "pulse",
            pulse = board.Pulse,
            view = board.View,
            focus = board.Focus,
            mutation,
            promoted = IsPromoteOp(op) && !IsShareReportOp(op, args)
                ? mutation
                : IdePlanPromote.TryPulse(
                    Opt(args, "project_root") ?? OptGoArg(args, "project_root"),
                    Opt(args, "dir") ?? OptGoArg(args, "dir")),
            hint =
                "Feature=Intent, Task=Stage (WitDB). Stage @phase = soft affinity (not status). " +
                "REPL: feature|task Y @act|focus|done|park|drop|phase act|share report|share plan|promote|confirm|reject. " +
                "Session phase drives desk layout (hold: desk.layout.hold / layout_hold=)."
        };
    }

    static string Title(IReadOnlyDictionary<string, JsonElement> args)
    {
        var t = Opt(args, "title") ?? OptGoArg(args, "title") ?? Opt(args, "name") ?? OptGoArg(args, "name");
        if (t is { Length: > 0 })
            return t;
        // Positional leftovers from REPL go_args.q
        return OptGoArg(args, "q") ?? Opt(args, "q") ?? "";
    }

    static Guid? ResolveParent(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var guid = GuidArg(args, "parent_id")
                   ?? GuidArgGo(args, "parent_id")
                   ?? GuidArg(args, "parent")
                   ?? GuidArgGo(args, "parent");
        if (guid is not null)
            return guid;

        var under = Opt(args, "under")
                    ?? OptGoArg(args, "under")
                    ?? Opt(args, "parent_title")
                    ?? OptGoArg(args, "parent_title");
        // Bare parent= as title when not a GUID (GuidArg already returned null).
        under ??= Opt(args, "parent") ?? OptGoArg(args, "parent");
        if (under is not { Length: > 0 })
            return null;

        return store.FindStageIdByTitle(state, under)
               ?? throw new ArgumentException($"parent Task not found: {under}");
    }

    static Guid? GuidArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        var s = Opt(args, key);
        return Guid.TryParse(s, out var g) ? g : null;
    }

    static Guid? GuidArgGo(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        var s = OptGoArg(args, key);
        return Guid.TryParse(s, out var g) ? g : null;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static string? OptGoArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return null;
        if (!ga.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
