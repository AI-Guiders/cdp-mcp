#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=test_desk</c> / Meta <c>cdp_test_sa</c> — agent-native Test-SA (ADR-0012).
/// Partials: View (decide/next), Capture (snap), Models (Snap/norm).
/// Not <c>go=test</c>/<c>go=test_scene</c> (raw runner) and not EICAS <c>go=sa</c>.
/// </summary>
internal static partial class IdeTestSaChannel
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

        var active = verdict is "retest" or "need_more" or "discover" or "run";
        CideTestDeskLatch.Publish(
            active,
            pulse,
            verdict,
            okCount: snap.Last?.Passed ?? 0,
            totalCount: snap.Last?.Total ?? 0,
            failed: snap.Last?.Failed ?? 0,
            skipped: snap.Last?.Skipped ?? 0);

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
}
