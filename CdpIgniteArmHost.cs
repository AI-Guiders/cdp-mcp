#nullable enable
using static CdpMcp.IdeIgniteArmHost;
using System.Collections.Concurrent;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// AutoIgnition ARM — IDE-owned schedule. Agent arms; harness waits (timer/event) and CDT-injects.
/// Persist: %LocalAppData%/cdp-mcp/ignite-arms-{seat}.json (seat-scoped — no sibling ghost TimerLoop).
/// Partials: Fire, Persist, Parse, Models, Api (arm/list), Reclaim (wake/overdue).
/// </summary>
internal sealed partial class CdpIgniteArmHost
{
    /// <summary>Порт коллекций cdp-state.witdb (ADR-0219). Default — профильный state root;
    /// тесты инжектируют временный root своим стором.</summary>
    readonly ICdpStateStore Store;

    /// <summary>Id армов, известные хосту с последней загрузки/reload — diff против текущих Arms
    /// даёт drop-список для SyncArms (строки сиблингов не трогаются).</summary>
    HashSet<string> LoadedIds = new(StringComparer.OrdinalIgnoreCase);

    public const string StoreSchema = "ignite_arms/v1";

    readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    readonly object Gate = new();
    readonly ConcurrentDictionary<string, byte> Firing = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, CancellationTokenSource> FireTokens = new(StringComparer.OrdinalIgnoreCase);
    List<IgniteArm> Arms = [];
    bool Loaded;
    int HostStarted;
    CancellationTokenSource? HostCts;
    private readonly TimeProvider _time;

    internal CdpIgniteArmHost(TimeProvider? time = null, ICdpStateStore? store = null)
    {
        _time = time ?? TimeProvider.System;
        Store = store ?? new WitDbCdpStateStore(new ProfileStateRootProvider());
    }

    string ResolveSeat()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CDP_IGNITE_SEAT")?.Trim();
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;
        return IdeDeploy.ClassifySeat(IdeDeploy.ResolveSelfInstallRoot());
    }

    public string Seat => ResolveSeat();

    public string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        ResolveSeat() switch
        {
            "cdp-debug" => "ignite-arms-cdp-debug.json",
            "cdp" => "ignite-arms-cdp.json",
            _ => "ignite-arms-other.json"
        });

    public string LegacyStorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        "ignite-arms.json");

            public void EnsureStarted()
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



    internal bool HasContinuityArms()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a => a.Status is "armed" or "firing" or "awaiting" or ProviderBlockedStatus);
    }

    /// <summary>Lifecycle hooks — call after build/test complete. Non-blocking fire.</summary>
    public void Notify(string eventName, bool ok, string? pulse = null, string? detail = null)
    {
        EnsureStarted();
        var ev = NormalizeEvent(eventName);
        List<IgniteArm> hits;
        lock (Gate)
        {
            // Store SSOT across processes (ADR-0219 §DI.5): sibling processes (session bridge, durable
            // supervisor) arm through the shared store — adopt them before filtering, else event arms
            // armed by one process are invisible to the other (silent wake loss 2026-09-09).
            ReloadFromStoreUnlocked();
            hits = Arms.Where(a =>
                    a.Status == "armed"
                    && a.Event.Equals(ev, StringComparison.OrdinalIgnoreCase)
                    && (!a.OkOnly || ok)
                    && (a.DueUtc is null || a.DueUtc <= _time.GetUtcNow()))
                .Select(Clone)
                .ToList();
        }

                foreach (var arm in hits)
            QueueFire(arm, ok, pulse, detail);

        // NotificationCenter (ADR-0213 Stage 2): рассылка по подпискам ников.
        CideWakeDispatch.NotifyEvent(ev, ok, pulse, detail);
    }

    /// <summary>Durable worker must await CDT delivery before process exit.</summary>
    internal async Task WaitForArmDeliveryAsync(string armId, TimeSpan timeout)
    {
        var deadline = _time.GetUtcNow() + timeout;
        while (_time.GetUtcNow() < deadline)
        {
            var arm = Snapshot().FirstOrDefault(a => a.Id.Equals(armId, StringComparison.OrdinalIgnoreCase));
            if (arm?.Status is "fired" or "error")
                return;
            await Task.Delay(500).ConfigureAwait(false);
        }
    }
}
