#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>
/// Request/reply latch IPC for agent surface ops (CDP ↔ Glass WPF).
/// Paths match <c>CdpHabitatPaths</c> flat habitat (%LocalAppData%/cdp-mcp), not workspace seat.
/// </summary>
internal static class GlassSurfaceIpc
{
    public const string CmdFileName = "surface-cmd-LATEST.json";
    public const string ReplyFileName = "surface-reply-LATEST.json";

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string CmdPath => Path.Combine(StateRoot, CmdFileName);
    public static string ReplyPath => Path.Combine(StateRoot, ReplyFileName);

    static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    /// <summary>Write cmd, wait for matching reply id. Returns (ok, reply root, error code).</summary>
    public static (bool Ok, JsonElement? Reply, string? Error) Call(
        string op,
        JsonObject? args,
        int timeoutMs)
    {
        Directory.CreateDirectory(StateRoot);
        var id = Guid.NewGuid().ToString("N");
        var cmd = new JsonObject
        {
            ["schema"] = IdeGlassSurfaceChannel.Schema,
            ["id"] = id,
            ["op"] = op,
            ["stamped_utc"] = DateTimeOffset.UtcNow
        };
        if (args is not null)
            cmd["args"] = args;

        try
        {
            if (File.Exists(ReplyPath))
                File.Delete(ReplyPath);
        }
        catch
        {
            /* best-effort clear stale reply */
        }

        AtomicWrite(CmdPath, cmd.ToJsonString(WriteOpts));

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(ReplyPath))
                {
                    var text = File.ReadAllText(ReplyPath);
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement.Clone();
                    if (root.TryGetProperty("id", out var idEl)
                        && string.Equals(idEl.GetString(), id, StringComparison.Ordinal))
                    {
                        var ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
                        return (ok, root, ok ? null : root.TryGetProperty("error", out var e) ? e.GetString() : "surface_error");
                    }
                }
            }
            catch (IOException)
            {
                /* settle */
            }
            catch (JsonException)
            {
                /* partial write */
            }

            Thread.Sleep(40);
        }

        return (false, null, "surface_timeout");
    }

    static void AtomicWrite(string path, string json)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
