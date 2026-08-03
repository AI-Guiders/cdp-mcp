#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent BATCH-9: report|debug_sa|test_sa|build_sa|sys|ecl|review|alert soft-organ hosts.</summary>
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

    static string NormalizeAliasCompound(
        string raw,
        string[] aliases,
        (string Prefix, string Op)[] compounds,
        string canonical)
    {
        foreach (var (prefix, op) in compounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return canonical + " " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return canonical + rest;

            var trimmed = rest.TrimStart();
            if (trimmed.Length == 0 || trimmed.Contains('=', StringComparison.Ordinal))
                return canonical + " " + op + rest;

            return canonical + rest;
        }

        foreach (var alias in aliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return canonical;
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return canonical + " " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals(canonical, StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith(canonical + " ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] ReportCompounds =
    [
        ("report_board", "scene"),
        ("cdp_report", "scene")
    ];

    static readonly string[] ReportAliases = ["report_board", "cdp_report"];

    static readonly (string Prefix, string Op)[] DebugSaCompounds =
    [
        ("debug_sa", "pulse"),
        ("debug_desk", "pulse"),
        ("cdp_debug_sa", "pulse")
    ];

    static readonly string[] DebugSaAliases = ["debug_desk", "cdp_debug_sa"];

    static readonly (string Prefix, string Op)[] TestSaCompounds =
    [
        ("test_sa", "pulse"),
        ("test_desk", "pulse"),
        ("cdp_test_sa", "pulse")
    ];

    static readonly string[] TestSaAliases = ["test_desk", "cdp_test_sa"];

    static readonly (string Prefix, string Op)[] BuildSaCompounds =
    [
        ("build_sa", "pulse"),
        ("build_desk", "pulse"),
        ("cdp_build_sa", "pulse")
    ];

    static readonly string[] BuildSaAliases = ["build_desk", "cdp_build_sa"];

    static readonly (string Prefix, string Op)[] SysCompounds =
    [
        ("sys_organ", "scene"),
        ("cdp_sys", "scene")
    ];

    static readonly string[] SysAliases = ["sys_organ", "cdp_sys"];

    static readonly (string Prefix, string Op)[] EclCompounds =
    [
        ("ecl_organ", "run"),
        ("cdp_ecl", "run"),
        ("chk_organ", "run")
    ];

    static readonly string[] EclAliases = ["chk", "ecl_organ", "cdp_ecl"];

    static readonly (string Prefix, string Op)[] ReviewCompounds =
    [
        ("review_organ", "board"),
        ("cdp_review", "board")
    ];

    static readonly string[] ReviewAliases = ["review_organ", "cdp_review"];

    static readonly (string Prefix, string Op)[] AlertCompounds =
    [
        ("alert_channel", "pulse"),
        ("cdp_alert", "pulse")
    ];

    static readonly string[] AlertAliases = ["eicas", "alert_channel", "cdp_alert"];

    static string NormalizeReportOp(string op) =>
        op switch { "desk" or "status" or "probe" or "a" => "scene", _ => op };

    static string NormalizeSysOp(string op) =>
        op switch { "desk" or "status" or "a" or "map" or "pulse" => "scene", _ => op };

    static string NormalizeEclOp(string op) =>
        op switch
        {
            "desk" or "status" or "scene" or "active" => "run",
            "catalog" => "list",
            "rm" or "delete" => "remove",
            "on" => "enable",
            "off" => "disable",
            "done" or "check" => "ack",
            _ => op
        };

    static string NormalizeReviewOp(string op) =>
        op switch
        {
            "desk" or "status" or "scene" or "pulse" => "board",
            "list" or "index" => "files",
            "file" or "aim" => "open",
            _ => op
        };

    static bool IsReportOp(string? op) => op is "scene";
    static bool IsSysOp(string? op) => op is "scene";
    static bool IsEclOp(string? op) =>
        op is "run" or "list" or "add" or "remove" or "link" or "unlink" or "enable" or "disable" or "ack" or "unack" or "reset";
    static bool IsReviewOp(string? op) => op is "board" or "files" or "open";

    static bool IsReportIntent(string raw) => MatchesIntent(raw, "report", ReportAliases, ReportCompounds);
    static bool IsDebugSaIntent(string raw) => MatchesIntent(raw, "debug_sa", DebugSaAliases, DebugSaCompounds);
    static bool IsTestSaIntent(string raw) => MatchesIntent(raw, "test_sa", TestSaAliases, TestSaCompounds);
    static bool IsBuildSaIntent(string raw) => MatchesIntent(raw, "build_sa", BuildSaAliases, BuildSaCompounds);
    static bool IsSysIntent(string raw) => MatchesIntent(raw, "sys", SysAliases, SysCompounds);
    static bool IsEclIntent(string raw) => MatchesIntent(raw, "ecl", EclAliases, EclCompounds)
        || raw.Equals("chk", StringComparison.OrdinalIgnoreCase)
        || raw.StartsWith("chk ", StringComparison.OrdinalIgnoreCase);
    static bool IsReviewIntent(string raw) => MatchesIntent(raw, "review", ReviewAliases, ReviewCompounds);
    static bool IsAlertIntent(string raw) => MatchesIntent(raw, "alert", AlertAliases, AlertCompounds);

    static bool MatchesIntent(string raw, string canonical, string[] aliases, (string Prefix, string Op)[] compounds)
    {
        if (raw.Equals(canonical, StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith(canonical + " ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in aliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in compounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
