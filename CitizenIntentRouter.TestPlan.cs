#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent test_plan|cdp_test_plan — IdeSessionLifecycle.TestPlan without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteTestPlan(string raw)
    {
        var work = NormalizeTestPlanCompound(raw);
        var op = ExtractKeyedValue(work, "op")
            ?? ExtractKeyedValue(work, "cmd");

        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("test_plan ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_test_plan ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "preview" : op.Trim().ToLowerInvariant();
        op = NormalizeTestPlanOp(op);

        if (!IsTestPlanOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "test_plan_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file_path")
            ?? ExtractKeyedValue(work, "file")
            ?? ExtractKeyedValue(work, "solution_path");
        var filter = ExtractKeyedValue(work, "filter");
        var failedFirst = ExtractKeyedValue(work, "failed_first");

        return new Route(
            Verb.TestPlan,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Tool: filter,
            NewString: failedFirst,
            Go: "test_plan");
    }

    static string NormalizeTestPlanCompound(string raw)
    {
        foreach (var (prefix, inject) in TestPlanCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return inject.Length == 0 ? "test_plan" : "test_plan " + inject;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            if (inject.Length == 0)
                return "test_plan " + rest;
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "test_plan " + rest;
            return "test_plan " + inject + " " + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Inject)[] TestPlanCompounds =
    [
        ("test_plan_desk", ""),
        ("test_plan_preview", "op=preview"),
        ("test_plan_apply", "op=apply"),
        ("test_plan_draft", "op=preview"),
        ("test_plan_run", "op=apply"),
        ("cdp_test_plan_preview", "op=preview"),
        ("cdp_test_plan_apply", "op=apply"),
        ("cdp_test_plan", "")
    ];

    static string NormalizeTestPlanOp(string op) =>
        op switch
        {
            "draft" or "desk" or "status" or "show" or "pulse" or "scene" => "preview",
            "run" or "exec" => "apply",
            _ => op
        };

    static bool IsTestPlanOp(string? op) =>
        op is "preview" or "apply";
}
