#nullable enable
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;
internal static partial class IdeToolCallWatch
{
    /// <summary>Disarm <c>tool-wake-{callId}</c> and clear latch when this call armed it.</summary>
    internal static void ClearWakeArm(string callId, bool hadExceeded)
    {
        try
        {
            var id = $"tool-wake-{callId}";
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["id"] = JsonSerializer.SerializeToElement(id)
            };
            _ = IdeIgniteArmHost.Disarm(args);
            if (hadExceeded)
            {
                IdeFlightDataRecorder.RecordWake("wake_cancel", id, null, "clear_wake_arm_after_call");
            }
        }
        catch
        {
        /* best-effort */
        }

        if (hadExceeded)
            CideToolWatchLatch.Clear();
    }

    /// <summary>Per-call override <c>timeout_wake</c>/<c>wake_after</c>; else organ default; 0 = off.</summary>
    /// <summary>Per-call override <c>timeout_wake</c>/<c>wake_after</c>; else FDR overlay; else organ default; 0 = off.</summary>
    public static int ResolveThresholdSeconds(string toolName, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (TryParseWakeOverride(args, out var over))
            return over;
        var name = (toolName ?? "").Trim();
        if (IdeFdrThresholdPolicy.TryGetOverlaySeconds(name, out var overlay))
            return overlay;
        return StaticThresholdSeconds(name);
    }

    /// <summary>Compiled organ floors — ignore FDR overlay (used by suggest + as fallback).</summary>
    public static int StaticThresholdSeconds(string toolName)
    {
        var name = (toolName ?? "").Trim();
        if (name.Length == 0)
            return DefaultThresholdSeconds;
        // Never nest wake on ignite itself / trivial pulse tools.
        if (name is "cdp_ignite" or "cdp_health" or "cdp_pressure" or "ping" or "cdp_fdr" or "cdp_teeth" or "cdp_postmortem")
            return 0;
        return name switch
        {
            "cdp_cockpit" => 20,
            "cdp_work" => 20,
            "cdp_build" or "cdp_test" or "cdp_deploy" => 120,
            "cdp_shell_run" => 90,
            _ => DefaultThresholdSeconds
        };
    }

    public static bool TryParseWakeOverride(IReadOnlyDictionary<string, JsonElement> args, out int seconds)
    {
        seconds = 0;
        if (!args.TryGetValue("timeout_wake", out var el) && !args.TryGetValue("wake_after", out el) && !args.TryGetValue("wake_threshold", out el))
            return false;
        switch (el.ValueKind)
        {
            case JsonValueKind.False:
                seconds = 0;
                return true;
            case JsonValueKind.True:
                seconds = DefaultThresholdSeconds;
                return true;
            case JsonValueKind.Number:
                if (el.TryGetInt32(out var n))
                {
                    seconds = Math.Clamp(n, 0, 600);
                    return true;
                }

                return false;
            case JsonValueKind.String:
                var s = el.GetString()?.Trim() ?? "";
                if (s is "off" or "false" or "0" or "no")
                {
                    seconds = 0;
                    return true;
                }

                if (IdeIgniteArmHost.TryParseDuration(s, out var span))
                {
                    seconds = Math.Clamp((int)span.TotalSeconds, 0, 600);
                    return true;
                }

                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    seconds = Math.Clamp(parsed, 0, 600);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    static void OnThreshold(ThresholdHit hit)
    {
        ArmedForCall[hit.CallId] = 0;
        ThresholdHookForTests?.Invoke(hit);
        var pulse = $"tool-watch · {hit.Tool} >{hit.ThresholdSeconds}s · still running";
        CideToolWatchLatch.Publish(active: true, pulse: pulse, tool: hit.Tool, thresholdSeconds: hit.ThresholdSeconds, startedUtc: hit.StartedUtc);
        if (SuppressArmForTests)
            return;
        if (string.Equals(Environment.GetEnvironmentVariable("CDP_TOOL_WAKE_ARM"), "0", StringComparison.Ordinal))
            return;
        try
        {
            var charge = IdeIgniteChannel.ComposeToolWatchWakeCharge(hit.Tool, hit.ThresholdSeconds);
            var armArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1s"),
                ["charge"] = JsonSerializer.SerializeToElement("custom"),
                ["message"] = JsonSerializer.SerializeToElement(charge),
                ["task"] = JsonSerializer.SerializeToElement($"tool-watch:{hit.Tool}"),
                ["once"] = JsonSerializer.SerializeToElement(true),
                ["last_once"] = JsonSerializer.SerializeToElement(false),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
                ["wait_seconds"] = JsonSerializer.SerializeToElement(180),
                ["force"] = JsonSerializer.SerializeToElement(true),
                ["id"] = JsonSerializer.SerializeToElement($"tool-wake-{hit.CallId}")
            };
            _ = IdeIgniteArmHost.Arm(armArgs);
            IdeFlightDataRecorder.RecordWake("wake_arm", $"tool-wake-{hit.CallId}", hit.Tool, $"threshold={hit.ThresholdSeconds}s");
        }
        catch
        {
        /* best-effort — latch already published */
        }
    }

    public static string AnnotateResult(string text, string tool, int thresholdSeconds, int elapsedSeconds, bool exceededDuring)
    {
        if (!exceededDuring && elapsedSeconds < thresholdSeconds)
            return text;
        if (string.IsNullOrWhiteSpace(text))
            return text;
        try
        {
            var node = JsonNode.Parse(text);
            if (node is not JsonObject obj)
                return text;
            obj["wake"] = new JsonObject
            {
                ["schema"] = Schema,
                ["exceeded"] = true,
                ["tool"] = tool,
                ["threshold_s"] = thresholdSeconds,
                ["elapsed_s"] = elapsedSeconds,
                ["during_call"] = exceededDuring,
                ["hint"] = "wall time past timeout_wake — Autoi once-wake armed if idle; prefer start+poll for long organs"
            };
            return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return text;
        }
    }
}