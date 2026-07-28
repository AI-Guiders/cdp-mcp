#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Hard-deploy remount continuity: durable pending under %LocalAppData%/cdp-mcp,
/// consumed once on MCP process boot → AutoIgnition "initialized" wake (no poll).
/// </summary>
internal static class IdeRemountWake
{
    public const string Schema = "remount_wake/v1";
    public const string ArmIdPrefix = "remount-wake-";
    public const string ArmTask = "remount-initialized";
    public const string ChargeMode = "remount";

    /// <summary>Settle after Cursor remount before CDT inject.</summary>
    public static int DefaultDueSeconds { get; set; } = 8;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Test hook: redirect pending file root (default LocalAppData/cdp-mcp).</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string PendingPathForSeat(string seat) =>
        Path.Combine(StateRoot, $"remount-wake-{NormalizeSeat(seat)}.pending.json");

    public static string NormalizeSeat(string? seat) =>
        (seat ?? "").Trim().ToLowerInvariant() switch
        {
            "cdp-debug" or "debug" => "cdp-debug",
            "cdp" or "release" => "cdp",
            _ => string.IsNullOrWhiteSpace(seat) ? "other" : seat.Trim().ToLowerInvariant()
        };

    public static void MarkPending(string targetRoot, string reason = "hard_deploy")
    {
        var seat = IdeDeploy.ClassifySeat(targetRoot);
        Directory.CreateDirectory(StateRoot);
        var path = PendingPathForSeat(seat);
        var doc = new RemountPendingDoc
        {
            Schema = Schema,
            Seat = seat,
            Target = Path.GetFullPath(targetRoot),
            Reason = reason,
            StampedUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOpts));
    }

    /// <summary>Atomically consume pending for this seat (delete file). Returns false if none.</summary>
    public static bool TryConsumePending(string? seat, out RemountPendingDoc? pending)
    {
        pending = null;
        var path = PendingPathForSeat(NormalizeSeat(seat ?? IdeIgniteArmHost.Seat));
        if (!File.Exists(path))
            return false;

        try
        {
            var raw = File.ReadAllText(path);
            pending = JsonSerializer.Deserialize<RemountPendingDoc>(raw, JsonOpts)
                      ?? new RemountPendingDoc { Seat = NormalizeSeat(seat), Reason = "hard_deploy" };
        }
        catch
        {
            pending = new RemountPendingDoc
            {
                Seat = NormalizeSeat(seat),
                Reason = "hard_deploy",
                StampedUtc = DateTimeOffset.UtcNow
            };
        }

        try { File.Delete(path); }
        catch { /* best-effort — avoid double-fire if delete fails next boot may retry */ }

        return true;
    }

    public sealed class RemountPendingDoc
    {
        public string Schema { get; set; } = IdeRemountWake.Schema;
        public string Seat { get; set; } = "cdp";
        public string? Target { get; set; }
        public string Reason { get; set; } = "hard_deploy";
        public DateTimeOffset StampedUtc { get; set; }
    }
}
