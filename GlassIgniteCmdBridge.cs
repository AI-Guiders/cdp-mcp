#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Glass Intercom HUD Korry → habitat AutoI/HILD toggle.
/// Request: %LocalAppData%/cdp-mcp/ignite-cmd-LATEST.json
/// </summary>
internal static class GlassIgniteCmdBridge
{
    public const string Schema = "glass_ignite_cmd/v0";
    public const string FileName = "ignite-cmd-LATEST.json";

    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);
    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static CancellationTokenSource? Cts;
    static string? LastProcessedId;
    internal static string? RootOverrideForTests { get; set; }

    internal static void ResetProcessedForTests() => LastProcessedId = null;

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string RequestPath => Path.Combine(StateRoot, FileName);

    public static void Start()
    {
        Stop();
        var cts = new CancellationTokenSource();
        Volatile.Write(ref Cts, cts);
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    public static void Stop()
    {
        var cts = Interlocked.Exchange(ref Cts, null);
        if (cts is null)
            return;
        try
        {
            cts.Cancel();
            cts.Dispose();
        }
        catch
        {
            /* ignore */
        }
    }

    static async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TryProcessOnce();
            }
            catch
            {
                /* best-effort */
            }

            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal static bool TryProcessOnce()
    {
        if (!File.Exists(RequestPath))
            return false;

        CmdDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<CmdDoc>(File.ReadAllText(RequestPath), ReadOpts);
        }
        catch
        {
            return false;
        }

        if (doc is null
            || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(doc.Id)
            || string.IsNullOrWhiteSpace(doc.Op))
            return false;

        if (string.Equals(doc.Status, "done", StringComparison.OrdinalIgnoreCase)
            || string.Equals(doc.Status, "error", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(LastProcessedId, doc.Id, StringComparison.Ordinal))
            return false;

        LastProcessedId = doc.Id;
        try
        {
            ApplyOp(doc.Op.Trim());
            WriteStatus(doc, "done", null);
            return true;
        }
        catch (Exception ex)
        {
            WriteStatus(doc, "error", ex.Message);
            return true;
        }
    }

    static void ApplyOp(string op)
    {
        switch (op.ToLowerInvariant())
        {
            case "autonomous_on":
                // Folded/talk/halt: Glass Autoi Korry paints OFF while await_partner latch still holds.
                // Click ON must clear that latch (resume) — SetAutonomous alone leaves TALK/HALT face.
                IdeIgniteArmHost.Resume(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));
                IdeIgniteArmHost.SetAutonomous(true, "glass_hud");
                break;
            case "autonomous_off":
                IdeIgniteArmHost.SetAutonomous(false, "glass_hud");
                break;
            case "hild_on":
                IdeIgniteArmHost.SetHild(true, "glass_hud");
                break;
            case "hild_off":
                IdeIgniteArmHost.SetHild(false, "glass_hud");
                break;
            default:
                throw new InvalidOperationException("unknown_op:" + op);
        }
    }

    static void WriteStatus(CmdDoc src, string status, string? error)
    {
        try
        {
            var outDoc = new CmdDoc
            {
                Schema = Schema,
                Origin = src.Origin ?? "glass",
                Id = src.Id,
                Op = src.Op,
                Status = status,
                Error = error,
                StampedUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(outDoc, WriteOpts);
            var tmp = RequestPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, RequestPath, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    public sealed class CmdDoc
    {
        public string? Schema { get; set; }
        public string? Origin { get; set; }
        public string? Id { get; set; }
        public string? Op { get; set; }
        public string? Status { get; set; }
        public string? Error { get; set; }

        [JsonPropertyName("stamped_utc")]
        public DateTimeOffset StampedUtc { get; set; }
    }
}
