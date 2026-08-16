#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>Background lifecycle (build/test/deploy) ↔ AutoIgnition: auto-arm on start, notify on finish.</summary>
internal static class IdeLifecycleIgnite
{
    public const string BackgroundArmIdPrefix = "lifecycle-bg-";

    public static bool TryAutoArm(
        string whenEvent,
        string kind,
        string? targetHint,
        bool enabled,
        out string? armId)
    {
        armId = null;
        if (!enabled || IdeToolCallWatch.SuppressArmForTests)
            return false;
        if (string.Equals(Environment.GetEnvironmentVariable("CDP_LIFECYCLE_IGNITE_ARM"), "0", StringComparison.Ordinal))
            return false;

        var safeKind = string.IsNullOrWhiteSpace(kind) ? "job" : kind.Trim();
        armId = BackgroundArmIdPrefix + safeKind.ToLowerInvariant();
        var task = BuildTaskLabel(safeKind, targetHint);
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["when"] = JsonSerializer.SerializeToElement(whenEvent),
            ["task"] = JsonSerializer.SerializeToElement(task),
            ["id"] = JsonSerializer.SerializeToElement(armId),
            ["once"] = JsonSerializer.SerializeToElement(true),
            ["last_once"] = JsonSerializer.SerializeToElement(false),
            ["ok_only"] = JsonSerializer.SerializeToElement(false),
            ["force"] = JsonSerializer.SerializeToElement(true),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
        };

        try
        {
            var result = IdeIgniteArmHost.Arm(args);
            return TryReadArmOk(result);
        }
        catch
        {
            armId = null;
            return false;
        }
    }

    public static string AnnotateStarted(JsonSerializerOptions pretty, string json, string whenEvent, string? armId)
    {
        if (string.IsNullOrWhiteSpace(armId))
            return json;
        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is null)
                return json;
            node["ignite"] = new JsonObject
            {
                ["armed"] = true,
                ["when"] = whenEvent,
                ["arm_id"] = armId,
                ["hint"] = $"AutoIgnition wakes Composer when this {whenEvent} job completes (cdp_lifecycle_last kind=…)."
            };
            return node.ToJsonString(pretty);
        }
        catch
        {
            return json;
        }
    }

    static string BuildTaskLabel(string kind, string? targetHint)
    {
        var hint = Truncate((targetHint ?? "").Trim(), 96);
        return hint.Length == 0 ? $"lifecycle:{kind}" : $"{kind}: {hint}";
    }

    static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    static bool TryReadArmOk(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            return doc.RootElement.TryGetProperty("ok", out var ok)
                   && ok.ValueKind == JsonValueKind.True
                   && (!doc.RootElement.TryGetProperty("skipped", out var skip)
                       || skip.ValueKind != JsonValueKind.True);
        }
        catch
        {
            return false;
        }
    }
}
