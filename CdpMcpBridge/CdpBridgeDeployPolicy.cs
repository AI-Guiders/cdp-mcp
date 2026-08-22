using System.Text.Json;

namespace CdpMcpBridge;

internal static class CdpBridgeDeployPolicy
{
    internal static bool ShouldBridgeWait(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (IsTruthy(args, "dry_run") || IsTruthy(args, "peek"))
            return false;
        if (IsExplicitFalse(args, "bridge_wait"))
            return false;
        if (IsTruthy(args, "bridge_wait") || IsTruthy(args, "wait"))
            return true;
        if (IsExplicitFalse(args, "background"))
            return false;

        var mode = NormalizeMode(Opt(args, "mode") ?? "hard");
        return mode is "apply" or "hard" or "rollout";
    }

    internal static Dictionary<string, JsonElement> PrepareForwardDeployArgs(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var forward = args.ToDictionary(static p => p.Key, static p => p.Value, StringComparer.OrdinalIgnoreCase);
        forward.Remove("wait");
        forward.Remove("bridge_wait");
        forward["background"] = JsonSerializer.SerializeToElement(true);
        if (!forward.ContainsKey("durable"))
            forward["durable"] = JsonSerializer.SerializeToElement(true);
        return forward;
    }

    internal static string NormalizeMode(string mode) =>
        mode.Trim().ToLowerInvariant() switch
        {
            "soft" => "soft",
            "apply" => "apply",
            "hard" => "hard",
            "rollout" => "rollout",
            _ => "hard"
        };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static bool IsTruthy(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b
                                   || string.Equals(el.GetString(), "1", StringComparison.Ordinal),
            _ => false
        };
    }

    static bool IsExplicitFalse(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.False => true,
            JsonValueKind.String => string.Equals(el.GetString(), "false", StringComparison.OrdinalIgnoreCase)
                                    || el.GetString() == "0",
            JsonValueKind.Number => el.TryGetInt32(out var n) && n == 0,
            _ => false
        };
    }
}
