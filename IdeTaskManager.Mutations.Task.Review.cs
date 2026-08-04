#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskReview(IntentWorkspaceStore store, IntentWorkspaceState state, IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = ResolveNoteStageId(store, state, args)
                 ?? throw new ArgumentException("review needs active task — focus first");

        var action = (Opt(args, "action") ?? OptGoArg(args, "action") ?? Opt(args, "review_op") ?? OptGoArg(args, "review_op") ?? "")
            .Trim().ToLowerInvariant();
        var title = Title(args);
        var textHint = Opt(args, "text") ?? Opt(args, "body") ?? OptGoArg(args, "text") ?? OptGoArg(args, "body") ?? "";

        // Prefer explicit body → add (dialog stamp). Else parse title head for list|ack|<remark>.
        if (action.Length == 0)
        {
            if (textHint.Length > 0)
                action = "add";
            else if (title.Length > 0)
            {
                var parts = title.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var head = parts[0].ToLowerInvariant();
                if (head is "list" or "ls" or "open" or "scene")
                {
                    action = "list";
                    title = parts.Length > 1 ? parts[1] : "";
                }
                else if (head is "ack" or "close" or "done")
                {
                    action = "ack";
                    title = parts.Length > 1 ? parts[1] : "";
                }
                else if (head is "all")
                    return store.StageEventReviewList(state, id, openOnly: false);
                else
                    action = "add";
            }
            else
                action = "list";
        }

        return action switch
        {
            "list" or "ls" or "open" or "scene" => store.StageEventReviewList(state, id, openOnly: true),
            "all" => store.StageEventReviewList(state, id, openOnly: false),
            "ack" or "close" => AckOne(store, state, id, args, title),
            "add" or "note" or "put" => AddOne(store, state, id, args, title),
            _ => AddOne(store, state, id, args, action + (title.Length > 0 ? " " + title : ""))
        };
    }

    static object AddOne(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        Guid stageId,
        IReadOnlyDictionary<string, JsonElement> args,
        string titleFallback)
    {
        var text = Opt(args, "text") ?? Opt(args, "body") ?? OptGoArg(args, "text") ?? OptGoArg(args, "body") ?? "";
        if (text.Length == 0)
            text = titleFallback;
        var source = Opt(args, "source") ?? OptGoArg(args, "source") ?? "operator";
        return store.StageEventReviewAdd(state, stageId, text, source);
    }

    static object AckOne(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        Guid stageId,
        IReadOnlyDictionary<string, JsonElement> args,
        string titleFallback)
    {
        var raw = Opt(args, "review_id") ?? Opt(args, "id") ?? OptGoArg(args, "review_id") ?? OptGoArg(args, "id")
                  ?? titleFallback;
        raw = raw.Trim();
        if (raw.Length == 0)
            throw new ArgumentException("review ack needs id — review ack <review_id>");
        if (!Guid.TryParse(raw, out var reviewId)
            && !Guid.TryParseExact(raw, "N", out reviewId))
            throw new ArgumentException($"review ack: not a review_id: {raw}");
        var note = Opt(args, "text") ?? Opt(args, "body") ?? OptGoArg(args, "text") ?? OptGoArg(args, "body");
        return store.StageEventReviewAck(state, stageId, reviewId, note);
    }
}
