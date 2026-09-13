#nullable enable

using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Post-green-build ship bridge (ADR-0013 follow-up): embed <c>next[]</c> when tree is dirty.
/// </summary>
internal static class IdeBuildShipBridge
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    internal static string AnnotateBuildResult(string buildJson, SessionContext session)
    {
        if (!IsGreenLifecycle(buildJson))
            return buildJson;

        var (next, verdict) = IdeBuildSaChannel.TryShipNextAfterGreenBuild(session);
        if (next is null or { Length: 0 })
            return buildJson;

        return AnnotateJson(buildJson, next, verdict!, "post_build_ship");
    }

    internal static string AnnotateCsxRunReport(string reportJson, SessionContext session)
    {
        if (!TryParseReportOk(reportJson, out var ok) || !ok)
            return reportJson;

        if (!CsxReportIncludesGreenBuild(reportJson))
            return reportJson;

        var (next, verdict) = IdeBuildSaChannel.TryShipNextAfterGreenBuild(session);
        if (next is null or { Length: 0 })
            return reportJson;

        return AnnotateJson(reportJson, next, verdict!, "post_csx_verify_ship");
    }

    static string AnnotateJson(string json, object[] next, string verdict, string phase)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        if (node is null)
            return json;

        node["next"] = JsonSerializer.SerializeToNode(next, Pretty);
        node["bridge"] = new JsonObject
        {
            ["schema"] = "build_ship_bridge/v0",
            ["verdict"] = verdict,
            ["phase"] = phase,
            ["hint"] = "Dirty after green verify — git_plan / CSX Git.* / cdp_ship; not shell git."
        };
        return node.ToJsonString(Pretty);
    }

    static bool IsGreenLifecycle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return n == 0;
            if (root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var errors))
                return errors == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool TryParseReportOk(string reportJson, out bool ok)
    {
        ok = false;
        try
        {
            using var doc = JsonDocument.Parse(reportJson);
            if (!doc.RootElement.TryGetProperty("Ok", out var okEl)
                && !doc.RootElement.TryGetProperty("ok", out okEl))
                return false;
            ok = okEl.ValueKind == JsonValueKind.True;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool CsxReportIncludesGreenBuild(string reportJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(reportJson);
            if (!doc.RootElement.TryGetProperty("Steps", out var steps)
                && !doc.RootElement.TryGetProperty("steps", out steps))
                return false;

            foreach (var step in steps.EnumerateArray())
            {
                var underlying = StepString(step, "Underlying") ?? StepString(step, "underlying");
                if (!string.Equals(underlying, "build_structured", StringComparison.OrdinalIgnoreCase))
                    continue;

                var result = StepString(step, "Result") ?? StepString(step, "result");
                if (result is not null && IsGreenLifecycle(result))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    static string? StepString(JsonElement step, string name) =>
        step.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
