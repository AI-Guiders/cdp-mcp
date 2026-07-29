#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>
/// After <c>cdp_cockpit_host op=start</c>: re-stamp existing dual-cockpit latches so CIDE
/// projectors cold-apply Melody/settings-preserving glass (do not strip — ADR-0019).
/// Does not write operator settings.toml or Intent Melody catalog.
/// </summary>
internal static class CockpitHostLatchHydration
{
    static readonly string[] LatchNames =
    [
        "presentation-LATEST.json",
        "seats-LATEST.json",
        "land-LATEST.json",
        "disk-LATEST.json",
        "intercom-LATEST.json",
        "shared-LATEST.json",
        "alert-LATEST.json",
        "qrh-LATEST.json",
        "ecl-LATEST.json"
    ];

    /// <summary>Test hook: redirect flat latch root (same as glass latches).</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    /// <summary>Bump <c>stamped_utc</c> on existing latches; returns count touched.</summary>
    public static int TouchAgentLatchesForHostStart()
    {
        var touched = 0;
        try
        {
            Directory.CreateDirectory(StateRoot);
            foreach (var name in LatchNames)
            {
                if (TryBumpStamp(Path.Combine(StateRoot, name)))
                    touched++;
            }
        }
        catch
        {
            /* best-effort */
        }

        return touched;
    }

    static bool TryBumpStamp(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var raw = File.ReadAllText(path);
            var node = JsonNode.Parse(raw);
            if (node is not JsonObject obj)
                return false;

            obj["stamped_utc"] = DateTimeOffset.UtcNow.ToString("O");
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
