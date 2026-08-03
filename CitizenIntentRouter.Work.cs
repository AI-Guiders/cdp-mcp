#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent work|cdp_work — intent workspace without Cursor MCP. go=work/plan stays Verb.Go; cmd= stays Verb.Cmd.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteWork(string raw)
    {
        var work = NormalizeWorkCompound(raw);
        var op = ExtractKeyedValue(work, "op")
            ?? ExtractWorkPositionalOp(work)
            ?? "status";
        var title = ExtractKeyedValue(work, "title");
        var intentId = ExtractKeyedValue(work, "intent_id");
        var stageId = ExtractKeyedValue(work, "stage_id");
        var name = ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "scene_name");

        return new Route(
            Verb.Work,
            raw,
            Ok: true,
            Op: op.Trim().ToLowerInvariant(),
            Scene: string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Organ: string.IsNullOrWhiteSpace(intentId) ? null : intentId.Trim(),
            Detail: string.IsNullOrWhiteSpace(stageId) ? null : stageId.Trim(),
            Tool: string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Go: "intent_workspace");
    }

    static string? ExtractWorkPositionalOp(string work)
    {
        var rest = work.StartsWith("work ", StringComparison.OrdinalIgnoreCase)
            ? work["work ".Length..].Trim()
            : work;
        if (string.IsNullOrWhiteSpace(rest) || rest.Contains('=', StringComparison.Ordinal))
            return null;
        var token = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return WorkOps.Contains(token) ? token : null;
    }

    static string NormalizeWorkCompound(string raw)
    {
        foreach (var prefix in WorkPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "work";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "work " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly HashSet<string> WorkOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "intent_list", "stage_list", "scene_list",
        "intent_upsert", "intent_select", "intent_delete",
        "stage_upsert", "stage_get", "stage_set_status", "stage_delete",
        "scene_park", "scene_switch"
    };

    static readonly string[] WorkPrefixes =
    [
        "cdp_work",
        "intent_workspace",
        "work_desk",
        "work"
    ];
}
