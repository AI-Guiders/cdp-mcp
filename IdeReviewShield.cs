#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Operator Review Results — durable remarks on the leaf must be dug before Done.
/// Capture is dialog (agent stamps <c>review</c>); not a Glass form roundtrip.
/// </summary>
internal static class IdeReviewShield
{
    internal const string RefuseId = "open_operator_reviews";

    internal static void RefuseDoneWithOpenReviews(
        IntentWorkspaceStore store,
        Guid stageId,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (ForceArg(args))
            return;

        int open;
        try
        {
            open = store.StageEventOpenReviewCount(stageId);
        }
        catch
        {
            return;
        }

        if (open <= 0)
            return;

        var tips = store.StageEventOpenReviewSummaries(stageId, take: 3);
        var sample = tips.Count == 0
            ? ""
            : " · " + string.Join(" | ", tips.Select(t => t.Length > 40 ? t[..39] + "…" : t));
        throw new ArgumentException(
            $"task_done refused — {RefuseId}: {open} open operator review(s). " +
            $"Dig: cmd=\"review list\" then address + ack <review_id>. force=true escape.{sample}");
    }

    static bool Boolish(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.True)
            return true;
        return el.ValueKind == JsonValueKind.String
               && bool.TryParse(el.GetString(), out var b)
               && b;
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (Boolish(args, "force"))
            return true;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var f))
        {
            if (f.ValueKind == JsonValueKind.True)
                return true;
            if (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var b) && b)
                return true;
        }

        return false;
    }
}
