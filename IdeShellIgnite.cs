#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Background shell ↔ AutoIgnition: auto-arm on <c>background=true</c>, notify on finish.</summary>
internal static class IdeShellIgnite
{
    public const string BackgroundArmIdPrefix = "shell-bg-";

    public static void OnShellFinished(ShellFinishedInfo info)
    {
        if (!info.Background)
            return;

        IdeIgniteArmHost.Notify(
            "shell_finished",
            ok: info.ExitCode == 0,
            pulse: info.Tab,
            detail: Truncate(info.Command, 120));

        if (info.ExitCode != 0)
            IdeStageCycle.TryAppend("shell.fail", "shell", info.Command, info.Tab);
    }

  /// <summary>Arm <c>when=shell_finished</c> for a background tab job (replaces prior arm on same tab).</summary>
    public static bool TryAutoArmBackground(
        string? tab,
        string? command,
        bool enabled,
        out string? armId)
    {
        armId = null;
        if (!enabled || IdeToolCallWatch.SuppressArmForTests)
            return false;
        if (string.Equals(Environment.GetEnvironmentVariable("CDP_SHELL_IGNITE_ARM"), "0", StringComparison.Ordinal))
            return false;

        var safeTab = string.IsNullOrWhiteSpace(tab) ? "main" : tab.Trim();
        armId = BackgroundArmIdPrefix + safeTab.ToLowerInvariant();
        var task = BuildTaskLabel(safeTab, command);
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["when"] = JsonSerializer.SerializeToElement("shell_finished"),
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

    public static string AnnotateBackgroundRun(JsonSerializerOptions pretty, string json, string? armId)
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
                ["when"] = "shell_finished",
                ["arm_id"] = armId,
                ["hint"] = "AutoIgnition wakes Composer when this background tab exits (cdp_shell_last tab=…)."
            };
            return node.ToJsonString(pretty);
        }
        catch
        {
            return json;
        }
    }

    internal static bool ResolveIgniteArmEnabled(IReadOnlyDictionary<string, JsonElement> callArgs, bool background)
    {
        if (!background)
            return false;
        if (callArgs.TryGetValue("ignite_arm", out var el))
        {
            return el.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.String => !IsFalseToken(el.GetString()),
                JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
                _ => true
            };
        }

        return true;
    }

    static bool IsFalseToken(string? raw) =>
        raw is "0" or "false" or "no" or "off";

    static string BuildTaskLabel(string tab, string? command)
    {
        var cmd = Truncate((command ?? "").Trim(), 96);
        if (cmd.Length == 0)
            return $"shell:{tab}";
        return tab.Equals("main", StringComparison.OrdinalIgnoreCase)
            ? $"shell: {cmd}"
            : $"shell:{tab}: {cmd}";
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
