#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=report</c> — evidence board from ScriptScene last run (ADR 0193).
/// Named IdeReportBoard to avoid clash with Cdp.ScriptableIde.IdeReport.
/// </summary>
internal static class IdeReportBoard
{
    public const string SchemaVersion = "report_board/v1";

    public static object Handle(SessionContext session, IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = args;
        var last = ScriptScene.TryGetLast(session);
        if (last is null)
        {
            // Idle is not FAIL — cold/remount comfort (0.5.174).
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "report",
                go = "report",
                detail = "pulse",
                idle = true,
                pulse = "report · idle",
                view = new
                {
                    schema = SchemaVersion,
                    lines = new[] { "(idle — cmd=\"probe\" | check | run)" }
                },
                hint = "No evidence yet — probe when ready. Not an error."
            };
        }

        object? body = null;
        if (last.BodyJson is { Length: > 0 })
        {
            try { body = JsonSerializer.Deserialize<JsonElement>(last.BodyJson); }
            catch { body = last.BodyJson; }
        }

        return new
        {
            ok = last.Ok,
            schema = SchemaVersion,
            role = "report",
            go = "report",
            detail = "pulse",
            idle = false,
            pulse = last.Pulse,
            path = last.Path,
            mode = last.Mode,
            at_utc = last.AtUtc,
            view = new
            {
                schema = SchemaVersion,
                lines = last.Board
            },
            body,
            hint = "Evidence from last check/run. pane_full=report for JSON; probe to refresh."
        };
    }

    public static string PulseLine(SessionContext session)
    {
        var last = ScriptScene.TryGetLast(session);
        return last?.Pulse ?? "report · idle";
    }

    public static bool HasEvidence(SessionContext session) =>
        ScriptScene.TryGetLast(session) is not null;

    /// <summary>Mirror report board pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass(SessionContext session)
    {
        try
        {
            var last = ScriptScene.TryGetLast(session);
            if (last is null)
            {
                CideReportLatch.Publish(active: false, pulse: "report · idle", path: null, mode: null, ok: null);
                return;
            }

            // Dark Cockpit: silent when idle (no evidence).
            CideReportLatch.Publish(
                active: true,
                pulse: last.Pulse,
                path: last.Path,
                mode: last.Mode,
                ok: last.Ok);
        }
        catch
        {
            /* best-effort */
        }
    }
}
