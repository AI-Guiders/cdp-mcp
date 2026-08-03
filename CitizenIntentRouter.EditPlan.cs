#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent edit_plan|cdp_edit_plan — EditorPlane plan without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteEditPlan(string raw)
    {
        var work = NormalizeEditPlanCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("edit_plan ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_edit_plan ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "draft" : op.Trim().ToLowerInvariant();
        op = NormalizeEditPlanOp(op);

        if (!IsEditPlanOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "edit_plan_op_unknown");

        var yaml = ExtractKeyedValue(work, "yaml")
            ?? ExtractKeyedValue(work, "slices_yaml")
            ?? ExtractKeyedValue(work, "plan")
            ?? ExtractKeyedValue(work, "body");
        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file_path")
            ?? ExtractKeyedValue(work, "file");
        var sketch = ExtractKeyedValue(work, "sketch");

        if ((op is "validate" or "apply") && string.IsNullOrWhiteSpace(yaml))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "edit_plan_yaml_required");

        return new Route(
            Verb.EditPlan,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Tool: sketch,
            NewString: yaml,
            Go: "edit_plan");
    }

    static string NormalizeEditPlanCompound(string raw)
    {
        foreach (var (prefix, inject) in EditPlanCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return inject.Length == 0 ? "edit_plan" : "edit_plan " + inject;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            if (inject.Length == 0)
                return "edit_plan " + rest;
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "edit_plan " + rest;
            return "edit_plan " + inject + " " + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Inject)[] EditPlanCompounds =
    [
        ("edit_plan_desk", ""),
        ("edit_plan_draft", "op=draft"),
        ("edit_plan_validate", "op=validate"),
        ("edit_plan_preview", "op=preview"),
        ("edit_plan_apply", "op=apply"),
        ("cdp_edit_plan_draft", "op=draft"),
        ("cdp_edit_plan_validate", "op=validate"),
        ("cdp_edit_plan_preview", "op=preview"),
        ("cdp_edit_plan_apply", "op=apply")
    ];

    static string NormalizeEditPlanOp(string op) =>
        op switch
        {
            "preview" => "validate",
            "desk" or "status" or "show" or "pulse" => "draft",
            _ => op
        };

    static bool IsEditPlanOp(string? op) =>
        op is "draft" or "validate" or "apply";
}
