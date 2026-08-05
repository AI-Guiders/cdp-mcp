namespace CdpMcp;
internal static partial class CitizenIntentRouter
{
    static string NormalizeAliasCompound(string raw, string[] aliases, (string Prefix, string Op)[] compounds, string canonical)
    {
        foreach (var(prefix, op)in compounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return canonical + " " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op")is { Length: > 0 })
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

        if (raw.Equals(canonical, StringComparison.OrdinalIgnoreCase) || raw.StartsWith(canonical + " ", StringComparison.OrdinalIgnoreCase))
            return raw;
        return raw;
    }

    static readonly (string Prefix, string Op)[] ReportCompounds = [("report_board", "scene"), ("cdp_report", "scene")];
    static readonly string[] ReportAliases = ["report_board", "cdp_report"];
    static readonly (string Prefix, string Op)[] DebugSaCompounds = [("debug_sa", "pulse"), ("debug_desk", "pulse"), ("cdp_debug_sa", "pulse")];
    static readonly string[] DebugSaAliases = ["debug_desk", "cdp_debug_sa"];
    static readonly (string Prefix, string Op)[] TestSaCompounds = [("test_sa", "pulse"), ("test_desk", "pulse"), ("cdp_test_sa", "pulse")];
    static readonly string[] TestSaAliases = ["test_desk", "cdp_test_sa"];
    static readonly (string Prefix, string Op)[] BuildSaCompounds = [("build_sa", "pulse"), ("build_desk", "pulse"), ("cdp_build_sa", "pulse")];
    static readonly string[] BuildSaAliases = ["build_desk", "cdp_build_sa"];
    static readonly (string Prefix, string Op)[] SysCompounds = [("sys_organ", "scene"), ("cdp_sys", "scene")];
    static readonly string[] SysAliases = ["sys_organ", "cdp_sys"];
    static readonly (string Prefix, string Op)[] EclCompounds = [("ecl_organ", "run"), ("cdp_ecl", "run"), ("chk_organ", "run")];
    static readonly string[] EclAliases = ["chk", "ecl_organ", "cdp_ecl"];
    static readonly (string Prefix, string Op)[] ReviewCompounds = [("review_organ", "board"), ("cdp_review", "board")];
    static readonly string[] ReviewAliases = ["review_organ", "cdp_review"];
    static readonly (string Prefix, string Op)[] AlertCompounds = [("alert_channel", "pulse"), ("cdp_alert", "pulse")];
    static readonly string[] AlertAliases = ["eicas", "alert_channel", "cdp_alert"];
    static string NormalizeReportOp(string op) => op switch
    {
        "desk" or "status" or "probe" or "a" => "scene",
        _ => op
    };
    static string NormalizeSysOp(string op) => op switch
    {
        "desk" or "status" or "a" or "map" or "pulse" => "scene",
        _ => op
    };
    static string NormalizeEclOp(string op) => op switch
    {
        "desk" or "status" or "scene" or "active" => "run",
        "catalog" => "list",
        "rm" or "delete" => "remove",
        "on" => "enable",
        "off" => "disable",
        "done" or "check" => "ack",
        _ => op
    };
    static string NormalizeReviewOp(string op) => op switch
    {
        "desk" or "status" or "scene" or "pulse" => "board",
        "list" or "index" => "files",
        "file" or "aim" => "open",
        _ => op
    };
    static bool IsReportOp(string? op) => op is "scene";
    static bool IsSysOp(string? op) => op is "scene";
    static bool IsEclOp(string? op) => op is "run" or "list" or "add" or "remove" or "link" or "unlink" or "enable" or "disable" or "ack" or "unack" or "reset";
    static bool IsReviewOp(string? op) => op is "board" or "files" or "open";
    static bool IsReportIntent(string raw) => MatchesIntent(raw, "report", ReportAliases, ReportCompounds);
    static bool IsDebugSaIntent(string raw) => MatchesIntent(raw, "debug_sa", DebugSaAliases, DebugSaCompounds);
    static bool IsTestSaIntent(string raw) => MatchesIntent(raw, "test_sa", TestSaAliases, TestSaCompounds);
    static bool IsBuildSaIntent(string raw) => MatchesIntent(raw, "build_sa", BuildSaAliases, BuildSaCompounds);
    static bool IsSysIntent(string raw) => MatchesIntent(raw, "sys", SysAliases, SysCompounds);
    static bool IsEclIntent(string raw) => MatchesIntent(raw, "ecl", EclAliases, EclCompounds) || raw.Equals("chk", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("chk ", StringComparison.OrdinalIgnoreCase);
    static bool IsReviewIntent(string raw) => MatchesIntent(raw, "review", ReviewAliases, ReviewCompounds);
    static bool IsAlertIntent(string raw) => MatchesIntent(raw, "alert", AlertAliases, AlertCompounds);
    static bool MatchesIntent(string raw, string canonical, string[] aliases, (string Prefix, string Op)[] compounds)
    {
        if (raw.Equals(canonical, StringComparison.OrdinalIgnoreCase) || raw.StartsWith(canonical + " ", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var alias in aliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase) || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var(prefix, _)in compounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase) || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}