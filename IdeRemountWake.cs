#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Hard-deploy / seat-recover remount continuity: durable pending under %LocalAppData%/cdp-mcp,
/// consumed once on MCP process boot → AutoIgnition "initialized" wake (no poll).
/// </summary>
internal static class IdeRemountWake
{
    public const string Schema = "remount_wake/v1";
    public const string ArmIdPrefix = "remount-wake-";
    public const string ArmTask = "remount-initialized";
    public const string ChargeMode = "remount";
    /// <summary>Machine-readable wake reason for agent (composer + arm SSOT).</summary>
    public const string Reason = "remount";

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

    /// <summary>True when hard-deploy left a remount pending for this seat (not yet consumed).</summary>
    public static bool HasPending(string? seat = null) =>
        File.Exists(PendingPathForSeat(NormalizeSeat(seat ?? IdeIgniteArmHost.Seat)));

    /// <summary>True when any seat has unconsumed remount pending (suppress false OOM on deploy blips).</summary>
    public static bool HasAnyPending()
    {
        if (!Directory.Exists(StateRoot))
            return false;
        try
        {
            return Directory.GetFiles(StateRoot, "remount-wake-*.pending.json").Length > 0;
        }
        catch
        {
            return false;
        }
    }

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
        IdeTeethTape.Record(
            "deploy_hard",
            detail: reason,
            reason: Reason);
    }

    /// <summary>Atomically consume pending for this seat (delete file). Returns false if none.
    /// Also consumes orphan <c>other</c> pending when Target matches this install (pre-0.5.661
    /// ClassifySeat miss left remount-wake-other.pending.json forever).</summary>
    public static bool TryConsumePending(string? seat, out RemountPendingDoc? pending)
    {
        pending = null;
        var normalized = NormalizeSeat(seat ?? IdeIgniteArmHost.Seat);
        if (TryConsumePendingExact(normalized, out pending))
            return true;

        return TryConsumeOrphanMatchingSelf(normalized, out pending);
    }

    static bool TryConsumePendingExact(string seat, out RemountPendingDoc? pending)
    {
        pending = null;
        var path = PendingPathForSeat(seat);
        if (!File.Exists(path))
            return false;

        try
        {
            var raw = File.ReadAllText(path);
            pending = JsonSerializer.Deserialize<RemountPendingDoc>(raw, JsonOpts)
                      ?? new RemountPendingDoc { Seat = seat, Reason = "hard_deploy" };
        }
        catch
        {
            pending = new RemountPendingDoc
            {
                Seat = seat,
                Reason = "hard_deploy",
                StampedUtc = DateTimeOffset.UtcNow
            };
        }

        try { File.Delete(path); }
        catch { /* best-effort — avoid double-fire if delete fails next boot may retry */ }

        return true;
    }

    static bool TryConsumeOrphanMatchingSelf(string seat, out RemountPendingDoc? pending)
    {
        pending = null;
        if (!Directory.Exists(StateRoot))
            return false;

        string[] orphans;
        try
        {
            orphans = Directory.GetFiles(StateRoot, "remount-wake-*.pending.json");
        }
        catch
        {
            return false;
        }

        var exactPath = PendingPathForSeat(seat);
        foreach (var path in orphans)
        {
            // Exact seat file already attempted.
            if (string.Equals(path, exactPath, StringComparison.OrdinalIgnoreCase))
                continue;

            RemountPendingDoc? doc;
            try
            {
                doc = JsonSerializer.Deserialize<RemountPendingDoc>(File.ReadAllText(path), JsonOpts);
            }
            catch
            {
                continue;
            }

            if (doc?.Target is not { Length: > 0 } target)
                continue;

            // Target path classifies to this boot seat (e.g. ...\cdp-mcp\self → cdp after 0.5.661).
            if (!IdeDeploy.ClassifySeat(target).Equals(seat, StringComparison.OrdinalIgnoreCase))
                continue;

            pending = doc;
            try { File.Delete(path); }
            catch { /* best-effort */ }
            return true;
        }

        return false;
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
