#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// AutoIgnition ARM — IDE-owned schedule. Agent arms; harness waits (timer/event) and CDT-injects.
/// Persist: %LocalAppData%/cdp-mcp/ignite-arms-{seat}.json (seat-scoped — no sibling ghost TimerLoop).
/// Partials: Fire, Persist, Parse, Models, Api (arm/list), Reclaim (wake/overdue).
/// </summary>
internal static partial class IdeIgniteArmHost
{
    public const string StoreSchema = "ignite_arms/v1";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    static readonly object Gate = new();
    static readonly ConcurrentDictionary<string, byte> Firing = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, CancellationTokenSource> FireTokens = new(StringComparer.OrdinalIgnoreCase);
    static List<IgniteArm> Arms = [];
    static bool Loaded;
    static int HostStarted;
    static CancellationTokenSource? HostCts;

    static string ResolveSeat()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CDP_IGNITE_SEAT")?.Trim();
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;
        return IdeDeploy.ClassifySeat(IdeDeploy.ResolveSelfInstallRoot());
    }

    public static string Seat => ResolveSeat();

    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        ResolveSeat() switch
        {
            "cdp-debug" => "ignite-arms-cdp-debug.json",
            "cdp" => "ignite-arms-cdp.json",
            _ => "ignite-arms-other.json"
        });

    public static string LegacyStorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        "ignite-arms.json");

            public static void EnsureStarted()
    {
        EnsureLoaded();
        var first = Interlocked.Exchange(ref HostStarted, 1) == 0;
        if (!first) return;
        // Remount / process boot: unstick overdue + mid-fire arms before TimerLoop.
        ReclaimOverdue(TimeSpan.FromSeconds(3));
        if (IdeIgniteWakeLatch.BootRefreshEnabled)
        {
            IdePressureChannel.TrySanitizeStashCourseOnBoot();
            IdeIgniteWakeLatch.RefreshCanonicalIfStale();
        }
        // Hard-deploy pending → one "MCP remounted / initialized" Autoi charge (no health poll).
        TryScheduleRemountInitializedWake();
        HostCts = new CancellationTokenSource();
        _ = Task.Run(() => TimerLoopAsync(HostCts.Token));
        // HILD watch starts from Program (not here) — EnsureStarted runs under unit tests.
    }



    internal static bool HasContinuityArms()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a => a.Status is "armed" or "firing" or "awaiting" or ProviderBlockedStatus);
    }

    /// <summary>Lifecycle hooks — call after build/test complete. Non-blocking fire.</summary>
    public static void Notify(string eventName, bool ok, string? pulse = null, string? detail = null)
    {
        EnsureStarted();
        var ev = NormalizeEvent(eventName);
        List<IgniteArm> hits;
        lock (Gate)
        {
            hits = Arms.Where(a =>
                    a.Status == "armed"
                    && a.Event.Equals(ev, StringComparison.OrdinalIgnoreCase)
                    && (!a.OkOnly || ok)
                    && (a.DueUtc is null || a.DueUtc <= DateTimeOffset.UtcNow))
                .Select(Clone)
                .ToList();
        }

                foreach (var arm in hits)
            QueueFire(arm, ok, pulse, detail);

        // NotificationCenter (ADR-0213 Stage 2): рассылка по подпискам ников.
        CideWakeDispatch.NotifyEvent(ev, ok, pulse, detail);
    }

    /// <summary>Durable worker must await CDT delivery before process exit.</summary>
    internal static async Task WaitForArmDeliveryAsync(string armId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var arm = Snapshot().FirstOrDefault(a => a.Id.Equals(armId, StringComparison.OrdinalIgnoreCase));
            if (arm?.Status is "fired" or "error")
                return;
            await Task.Delay(500).ConfigureAwait(false);
        }
    }
}
