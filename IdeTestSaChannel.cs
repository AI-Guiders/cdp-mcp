#nullable enable
using System.Text.Json;
using Cdp.Core;
using DotNetBuildTest.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=test_desk</c> / Meta <c>cdp_test_sa</c> — agent-native Test-SA (ADR-0012).
/// Not <c>go=test</c>/<c>go=test_scene</c> (raw runner) and not EICAS <c>go=sa</c>.
/// </summary>
internal static class IdeTestSaChannel
{
    public const string SchemaVersion = "test_sa/v1";
    public const string ToolName = "cdp_test_sa";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var depth = NormDepth(Opt(args, "depth") ?? "slim");
        var scope = NormScope(Opt(args, "scope") ?? "session");
        var snap = Capture(session, args);

        var (verdict, why) = Decide(snap, scope);
        var pulse = PulseLine(snap, verdict);

        if (depth == "pulse")
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "test_desk",
                go = "test_desk",
                tool = ToolName,
                detail = "pulse",
                pulse,
                verdict,
                why,
                scope,
                next = BuildNext(snap, verdict),
                hint = "depth=slim for last_run card. go=test_scene = raw map."
            };
        }

        object? failed = null;
        if (snap.Last is { FailedTests.Count: > 0 } last
            && (depth == "full" || scope == "failed" || verdict == "retest"))
        {
            failed = last.FailedTests.Take(depth == "full" ? 40 : 12).Select(f => new
            {
                name = f.Name,
                message = f.Message,
                duration_ms = f.DurationMs
            }).ToArray();
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "test_desk",
            go = "test_desk",
            tool = ToolName,
            detail = depth,
            pulse,
            verdict,
            why,
            scope,
            depth,
            target = snap.Target,
            last_run = snap.Last is null
                ? null
                : new
                {
                    at_utc = snap.Last.AtUtc,
                    success = snap.Last.Success,
                    total = snap.Last.Total,
                    passed = snap.Last.Passed,
                    failed = snap.Last.Failed,
                    skipped = snap.Last.Skipped,
                    filter = snap.Last.Filter
                },
            failed_tests = failed,
            next = BuildNext(snap, verdict),
            hint = depth == "full"
                ? "Full failed list when present. Act via cdp_test / cdp_test_plan — not shell."
                : "Slim Test-SA. depth=full for failed names; discover via next → test_scene."
        };
    }

    static string PulseLine(Snap snap, string verdict)
    {
        if (snap.Last is null)
            return $"test_desk · {verdict} · no last_run";
        return $"test_desk · {verdict} · {(snap.Last.Success ? "ok" : "FAIL")} {snap.Last.Passed}/{snap.Last.Total}";
    }

    static (string Verdict, string Why) Decide(Snap snap, string scope)
    {
        if (!snap.Ok)
            return ("need_more", snap.Error ?? "No project — cdp_open or path= before tests.");

        if (snap.Last is null)
            return scope == "last"
                ? ("discover", "No last_run — open test_scene to discover FQNs, then cdp_test.")
                : ("discover", "No last_run — discover via test_scene, or run cdp_test.");

        if (snap.Last.Failed > 0 || !snap.Last.Success)
            return ("retest", "last_run has failures — prefer cdp_test_plan failed_first=true.");

        if (scope == "failed")
            return ("green", "Scoped to failed but last_run is green — nothing to retest.");

        return ("green", "last_run green — leave, or rerun via cdp_test if intent demands.");
    }

    static object[] BuildNext(Snap snap, string verdict)
    {
        var list = new List<object>();
        switch (verdict)
        {
            case "discover":
                list.Add(new { go = "test_scene", label = "Discover", why = "cdp_test_scene — FQNs + last_run" });
                list.Add(new { go = "test", label = "Run", why = "cdp_test after discover" });
                break;
            case "retest":
                list.Add(new { go = "test_plan", label = "Retest failed", why = "cdp_test_plan failed_first=true" });
                list.Add(new { go = "test_scene", label = "Scene", why = "inspect last_run" });
                break;
            case "green":
                list.Add(new { go = "test_scene", label = "Scene", why = "confirm last_run" });
                list.Add(new { go = "review", label = "Review", why = "judgment after verify" });
                break;
            case "run":
                list.Add(new { go = "test", label = "Run", why = "cdp_test" });
                break;
            default:
                list.Add(new { go = "open", label = "cdp_open", why = "root project" });
                list.Add(new { go = "test_scene", label = "Scene", why = "after open" });
                break;
        }

        list.Add(new { go = "alert", label = "EICAS", why = "attention SA" });
        list.Add(new { go = "ecl", label = "ECL verify", why = "checklist" });
        return Dedup(list);
    }

    static object[] Dedup(List<object> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outList = new List<object>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var key = (t.GetProperty("label")?.GetValue(item) as string ?? "") + "\0" +
                      (t.GetProperty("why")?.GetValue(item) as string ?? "");
            if (!seen.Add(key)) continue;
            outList.Add(item);
        }

        return outList.ToArray();
    }

    static Snap Capture(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IdeSessionLifecycle.TryResolveTarget(session, args, out var target, out var err))
            return new Snap(false, err, null, null);

        var last = TestRunCache.TryGet(target);
        return new Snap(true, null, target, last);
    }

    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "failed" or "fail" or "failures" => "failed",
        "last" or "last_run" => "last",
        _ => "session"
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    sealed record Snap(
        bool Ok,
        string? Error,
        string? Target,
        TestRunCache.LastRun? Last);
}
