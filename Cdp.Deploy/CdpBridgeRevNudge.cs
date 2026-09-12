#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cdp.Deploy;

/// <summary>Cursor MCP remount without env — bump <c>--bridge-rev</c> in mcp.json args (ADR-0224).</summary>
public static class CdpBridgeRevNudge
{
    public const string ArgName = "--bridge-rev";

    public static bool TryBumpSeats(params string[] servers)
    {
        if (servers.Length == 0)
            return false;

        try
        {
            var mcpJson = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cursor",
                "mcp.json");
            if (!File.Exists(mcpJson))
                return false;

            var root = JsonNode.Parse(File.ReadAllText(mcpJson)) as JsonObject;
            if (root?["mcpServers"] is not JsonObject mcpServers)
                return false;

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var bumped = false;

            foreach (var server in servers)
            {
                if (mcpServers[server] is not JsonObject entry)
                    continue;

                if (BumpEntry(entry, stamp))
                    bumped = true;
            }

            if (!bumped)
                return false;

            File.WriteAllText(mcpJson, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool BumpEntry(JsonObject entry, string stamp)
    {
        var args = entry["args"] as JsonArray ?? new JsonArray();
        entry["args"] = args;

        var idx = IndexOf(args, ArgName);
        if (idx >= 0 && idx + 1 < args.Count)
            args[idx + 1] = stamp;
        else
        {
            args.Add(ArgName);
            args.Add(stamp);
        }

        if (entry["env"] is JsonObject env && env.ContainsKey("CDP_RELOAD_NUDGE"))
        {
            env.Remove("CDP_RELOAD_NUDGE");
            if (env.Count == 0)
                entry.Remove("env");
        }

        return true;
    }

    static int IndexOf(JsonArray args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i]?.GetValue<string>(), value, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
