#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent BATCH-9: report|debug_sa|test_sa|build_sa|sys|ecl|review|alert soft-instrument hosts.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteReport(string raw)
    {
        var work = NormalizeReportCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && work.StartsWith("report ", StringComparison.OrdinalIgnoreCase))
        {
            var sp = work.IndexOf(' ');
            var rest = work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeReportOp(op);
        if (!IsReportOp(op))
            return new Route(Verb.Report, raw, Ok: false, Reason: "report_op_unknown");

        return new Route(Verb.Report, raw, Ok: true, Op: op, Go: "report");
    }

    static Route RouteDebugSa(string raw)
    {
        var work = NormalizeDebugSaCompound(raw);
        if (!TryExtractSaDeskOp(raw, work, "debug_sa", Verb.DebugSa, out var op, out var fail))
            return fail!;

        return new Route(Verb.DebugSa, raw, Ok: true, Op: op, Go: "debug_desk");
    }

    static Route RouteTestSa(string raw)
    {
        var work = NormalizeTestSaCompound(raw);
        if (!TryExtractSaDeskOp(raw, work, "test_sa", Verb.TestSa, out var op, out var fail))
            return fail!;

        return new Route(Verb.TestSa, raw, Ok: true, Op: op, Go: "test_desk");
    }

    static Route RouteBuildSa(string raw)
    {
        var work = NormalizeBuildSaCompound(raw);
        if (!TryExtractSaDeskOp(raw, work, "build_sa", Verb.BuildSa, out var op, out var fail))
            return fail!;

        return new Route(Verb.BuildSa, raw, Ok: true, Op: op, Go: "build_desk");
    }

    static Route RouteSys(string raw)
    {
        var work = NormalizeSysCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && work.StartsWith("sys ", StringComparison.OrdinalIgnoreCase))
        {
            var sp = work.IndexOf(' ');
            var rest = work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeSysOp(op);
        if (!IsSysOp(op))
            return new Route(Verb.Sys, raw, Ok: false, Reason: "sys_op_unknown");

        return new Route(Verb.Sys, raw, Ok: true, Op: op, Go: "sys");
    }

    static Route RouteEcl(string raw)
    {
        var work = NormalizeEclCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && (work.StartsWith("ecl ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("chk ", StringComparison.OrdinalIgnoreCase)))
        {
            var sp = work.IndexOf(' ');
            var rest = work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "run" : op.Trim().ToLowerInvariant();
        op = NormalizeEclOp(op);
        if (!IsEclOp(op))
            return new Route(Verb.Ecl, raw, Ok: false, Reason: "ecl_op_unknown");

        var path = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "checklist")
            ?? ExtractKeyedValue(work, "item");

        return new Route(Verb.Ecl, raw, Ok: true, Op: op, Path: path, Go: "ecl");
    }

    static Route RouteReview(string raw)
    {
        var work = NormalizeReviewCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op)
            && work.StartsWith("review ", StringComparison.OrdinalIgnoreCase))
        {
            var sp = work.IndexOf(' ');
            var rest = work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "board" : op.Trim().ToLowerInvariant();
        op = NormalizeReviewOp(op);
        if (!IsReviewOp(op))
            return new Route(Verb.Review, raw, Ok: false, Reason: "review_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "file");

        return new Route(Verb.Review, raw, Ok: true, Op: op, Path: path, Go: "review");
    }

    static Route RouteAlert(string raw)
    {
        _ = NormalizeAlertCompound(raw);
        return new Route(Verb.Alert, raw, Ok: true, Op: "pulse", Go: "alert");
    }

    static bool TryExtractSaDeskOp(
        string raw,
        string work,
        string canonical,
        Verb verb,
        out string op,
        out Route? fail)
    {
        op = ExtractKeyedValue(work, "depth")
            ?? ExtractKeyedValue(work, "scope")
            ?? ExtractKeyedValue(work, "op")
            ?? ExtractKeyedValue(work, "cmd")
            ?? "";
        if (string.IsNullOrWhiteSpace(op)
            && work.StartsWith(canonical + " ", StringComparison.OrdinalIgnoreCase))
        {
            var sp = work.IndexOf(' ');
            var rest = work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "pulse" : op.Trim().ToLowerInvariant();
        if (!IsSaDeskShapeOp(op))
        {
            fail = new Route(verb, raw, Ok: false, Reason: canonical + "_shape_unknown");
            return false;
        }

        fail = null;
        return true;
    }

    static bool IsSaDeskShapeOp(string op) =>
        op is "pulse" or "slim" or "full" or "session" or "bp" or "stop" or "ship" or "failed";

    static string NormalizeReportCompound(string raw) => NormalizeAliasCompound(raw, ReportAliases, ReportCompounds, "report");
    static string NormalizeDebugSaCompound(string raw) => NormalizeAliasCompound(raw, DebugSaAliases, DebugSaCompounds, "debug_sa");
    static string NormalizeTestSaCompound(string raw) => NormalizeAliasCompound(raw, TestSaAliases, TestSaCompounds, "test_sa");
    static string NormalizeBuildSaCompound(string raw) => NormalizeAliasCompound(raw, BuildSaAliases, BuildSaCompounds, "build_sa");
    static string NormalizeSysCompound(string raw) => NormalizeAliasCompound(raw, SysAliases, SysCompounds, "sys");
    static string NormalizeEclCompound(string raw) => NormalizeAliasCompound(raw, EclAliases, EclCompounds, "ecl");
    static string NormalizeReviewCompound(string raw) => NormalizeAliasCompound(raw, ReviewAliases, ReviewCompounds, "review");
    static string NormalizeAlertCompound(string raw) => NormalizeAliasCompound(raw, AlertAliases, AlertCompounds, "alert");

}
