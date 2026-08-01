#nullable enable
using System.Globalization;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// HILD — Human-in-the-loop detector via CDT Composer.
/// Voice/empty + no Composer text for 5s → <c>human_away</c> once → AutoIgnition wake.
/// First away = partner status; still away after <see cref="AwayEscalateAfter"/> → autonomy.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    public const string HildArmIdPrefix = "hild-away-";
    /// <summary>Escalate wake — must fire even if the first away turn already ended.</summary>
    public const string HildEscalateArmIdPrefix = "hild-escalate-";
    /// <summary>Stable escalate wake id (like leaf-wake) — replaces prior; still matches prefix.</summary>
    public const string HildEscalateArmId = HildEscalateArmIdPrefix + "away";
    public const string HildEscalateChargeMode = "escalate";
    public const string HildEscalateReason = "escalate";
    public const string HildEscalateArmTask = "hild-away-escalate";
    public const string HildStoreSchema = "hild/v0";

    static readonly TimeSpan HildPollInterval = TimeSpan.FromSeconds(1);
    /// <summary>First away = status; still away after this → autonomy (partner likely gone long).</summary>
    internal static TimeSpan AwayEscalateAfter { get; set; } = TimeSpan.FromSeconds(60);

    static readonly IdeHildDetector HildDetector = new();
    static readonly object HildGate = new();

    static CancellationTokenSource? HildCts;
    static int HildPort = IdeIgniteChannel.DefaultPort;
    static bool HildArmed = true;
    static bool? HildOverride;
    static bool HildLoaded;
    static IdeHildDetector.Status HildLastStatus = IdeHildDetector.Status.Idle;
    static DateTimeOffset? HildLastEdgeUtc;
    static string? HildLastSampleKind;
    static int HildEdgeCount;
    static DateTimeOffset? AwayEscalateDueUtc;
    static bool AwayEscalateDone;

    public static string HildStorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        Seat switch
        {
            "cdp-debug" => "hild-cdp-debug.json",
            "cdp" => "hild-cdp.json",
            _ => "hild-other.json"
        });

    /// <summary>Tests: force armed without disk.</summary>
    internal static void BindHild(bool? armed) => HildOverride = armed;

    /// <summary>Test hook — detector instance.</summary>
    internal static IdeHildDetector HildDetectorForTests => HildDetector;

    public static bool IsHildArmed()
    {
        if (HildOverride is { } o)
            return o;
        EnsureHildLoaded();
        lock (HildGate)
            return HildArmed;
    }

    static void EnsureHildStarted()
    {
        EnsureHildLoaded();
        if (Volatile.Read(ref HildCts) is { IsCancellationRequested: false })
            return;

        var cts = new CancellationTokenSource();
        Volatile.Write(ref HildCts, cts);
        _ = Task.Run(() => HildLoopAsync(cts.Token));
    }

    /// <summary>Called from <see cref="EnsureStarted"/>.</summary>
    internal static void StartHildWatch()
    {
        EnsureHildStarted();
    }

    public static object Hild(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var opHint = (Opt(args, "mode") ?? Opt(args, "state") ?? "").Trim().ToLowerInvariant();
        var armedArg = OptBool(args, "armed");

        if (opHint is "off" or "disarm" or "clear" || armedArg == false)
            return SetHild(false, Opt(args, "why") ?? "operator/explicit off");
        if (opHint is "on" or "arm" || armedArg == true)
            return SetHild(true, Opt(args, "why") ?? "operator/explicit on");

        EnsureHildStarted();
        return HildStatusPayload();
    }

    public static object SetHild(bool armed, string? why = null)
    {
        EnsureHildLoaded();
        lock (HildGate)
        {
            HildArmed = armed;
            PersistHildUnlocked();
            if (!armed)
                HildDetector.Reset();
        }

        if (armed)
            EnsureHildStarted();

        return HildStatusPayload(why);
    }

    public static object HildStatusPayload(string? why = null)
    {
        EnsureHildLoaded();
        lock (HildGate)
        {
            return new
            {
                schema = HildStoreSchema,
                ok = true,
                op = "hild",
                go = IdeIgniteChannel.GoName,
                tool = IdeIgniteChannel.ToolName,
                pulse = $"hild · {(HildArmed ? "ARMED" : "DISARMED")} · {HildLastStatus} · edges={HildEdgeCount}",
                armed = HildArmed,
                status = HildLastStatus.ToString(),
                idle_seconds = IdeHildDetector.DefaultIdle.TotalSeconds,
                last_sample_kind = HildLastSampleKind,
                last_edge_utc = HildLastEdgeUtc,
                edge_count = HildEdgeCount,
                quiet_since = HildDetector.QuietSince,
                away_latched = HildDetector.AwayLatched,
                away_escalate_due_utc = AwayEscalateDueUtc,
                away_escalate_after_s = AwayEscalateAfter.TotalSeconds,
                why,
                hint =
                    "Voice/empty 5s → partner=away (status) + wake. Still away after AwayEscalateAfter → autonomous on. " +
                    "Human Composer text → partner=here. AutoI charge ignored as return."
            };
        }
    }

    static void EnsureHildLoaded()
    {
        if (HildLoaded)
            return;
        lock (HildGate)
        {
            if (HildLoaded)
                return;
            try
            {
                if (File.Exists(HildStorePath))
                {
                    var raw = File.ReadAllText(HildStorePath);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("armed", out var a)
                        && (a.ValueKind is JsonValueKind.True or JsonValueKind.False))
                        HildArmed = a.GetBoolean();
                }
            }
            catch
            {
                /* default armed */
            }

            HildLoaded = true;
        }
    }

    static void PersistHildUnlocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HildStorePath)!);
            var json = JsonSerializer.Serialize(new
            {
                schema = HildStoreSchema,
                armed = HildArmed,
                updated_utc = DateTimeOffset.UtcNow
            }, JsonOpts);
            File.WriteAllText(HildStorePath, json);
        }
        catch
        {
            /* best-effort */
        }
    }

    static async Task HildLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HildPollInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (!IsHildArmed())
                continue;

            try
            {
                await HildTickOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ide_ignite] hild probe failed: {ex.Message}");
            }
        }
    }

    static async Task HildTickOnceAsync(CancellationToken ct)
    {
        var sample = await IdeIgniteChannel.TrySampleComposerAsync(HildPort, ct).ConfigureAwait(false);
        if (!sample.Ok)
            return; // CDT blip — do not advance quiet clock

        IdeTeethTape.NoteGuest(sample.Kind, cdtUp: true);

        IdeHildDetector.TickResult tick;
        bool latchedBefore;
        bool latchedAfter;
        lock (HildGate)
        {
            HildLastSampleKind = sample.Kind;
            latchedBefore = HildDetector.AwayLatched;
            tick = HildDetector.Tick(new IdeHildDetector.Sample(
                sample.Kind,
                sample.Text,
                DateTimeOffset.UtcNow));
            HildLastStatus = tick.Status;
            latchedAfter = HildDetector.AwayLatched;
        }

        if (latchedBefore && !latchedAfter)
            OnPartnerHere();

        if (tick.EdgeHumanAway)
            OnHumanAwayEdge();
        else
            TryEscalateAwayToAutonomy();
    }

    static void OnPartnerHere()
    {
        lock (HildGate)
        {
            AwayEscalateDueUtc = null;
            AwayEscalateDone = false;
        }

        IdeTeethTape.Record("partner_here", detail: "hild_latch_cleared");
        Console.Error.WriteLine("[ide_ignite] hild partner here — away latch cleared");
    }

    static void OnHumanAwayEdge()
    {
        lock (HildGate)
        {
            HildLastEdgeUtc = DateTimeOffset.UtcNow;
            HildEdgeCount++;
            AwayEscalateDueUtc = DateTimeOffset.UtcNow + AwayEscalateAfter;
            AwayEscalateDone = false;
        }

        IdeTeethTape.Record("partner_away", detail: $"escalate_in={(int)AwayEscalateAfter.TotalSeconds}s");

        Console.Error.WriteLine(
            $"[ide_ignite] hild human_away edge #{HildEdgeCount} — status away; escalate@{AwayEscalateAfter.TotalSeconds:0}s");

        Notify("human_away", ok: true, pulse: "hild", detail: "composer_idle_5s");

        if (HasAwaitingOperatorLatch())
        {
            Console.Error.WriteLine("[ide_ignite] hild wake suppressed — await_operator latch");
            return;
        }

        if (HasArmedOomWake() || HasArmedRemountWake())
        {
            Console.Error.WriteLine("[ide_ignite] hild wake suppressed — oom/remount-wake armed");
            return;
        }

        SeedHildWake(out var hildArmId);
        IdeTeethTape.Record("wake_schedule", armId: hildArmId, reason: "hild", detail: "human_away");
    }

    /// <summary>
    /// Still away after <see cref="AwayEscalateAfter"/> → autonomy + escalate wake (reason=escalate).
    /// Autonomy latch alone is not enough — agent must receive a Composer charge if the first away turn ended.
    /// </summary>
    static void TryEscalateAwayToAutonomy()
    {
        // Claim under one lock — TOCTOU here scheduled a storm of escalate arms (dogfood 0.5.341).
        lock (HildGate)
        {
            if (AwayEscalateDone || AwayEscalateDueUtc is null || !HildDetector.AwayLatched)
                return;
            if (DateTimeOffset.UtcNow < AwayEscalateDueUtc.Value)
                return;
            AwayEscalateDone = true;
        }

        IdeTeethTape.Record("partner_away_escalate", detail: "still_away→autonomy+wake");
        SetAutonomous(true, "hild_away_escalate");
        var scheduled = TryScheduleHildEscalateWake();
        if (TryArmId(scheduled) is { } aid)
            IdeTeethTape.Record("wake_schedule", armId: aid, reason: HildEscalateReason, detail: "away_escalate");
        Console.Error.WriteLine(
            $"[ide_ignite] hild away escalate — still away after {AwayEscalateAfter.TotalSeconds:0}s → autonomous on + escalate wake");
    }

    /// <summary>One-shot timer charge_mode=escalate (system wake — not superseded).</summary>
    internal static object? TryScheduleHildEscalateWake()
    {
        EnsureLoaded();
        EnsureStarted();
        var dueSec = 2;
        var now = DateTimeOffset.UtcNow;

        IgniteArm arm;
        lock (Gate)
        {
            Arms.RemoveAll(a =>
                a.Id.StartsWith(HildEscalateArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");

            arm = new IgniteArm
            {
                Id = HildEscalateArmId,
                Event = "timer",
                Message = IdeIgniteChannel.ComposeEscalateWakeCharge(),
                ChargeMode = HildEscalateChargeMode,
                Task = HildEscalateArmTask,
                Reason = HildEscalateReason,
                Once = true,
                LastOnce = false,
                OkOnly = true,
                SettleSeconds = 1,
                WaitSeconds = 90,
                DueUtc = now + TimeSpan.FromSeconds(dueSec),
                InRaw = $"{dueSec}s",
                Status = "armed",
                CreatedUtc = now,
                LastError = "hild_away_escalate"
            };
            Arms.Add(arm);
            PersistUnlocked();
        }

        return Slim(arm);
    }

    static bool HasArmedOomWake()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a =>
                a.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");
    }

    static bool HasArmedRemountWake()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a =>
                a.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");
    }

    static bool HasAwaitingOperatorLatch()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a => a.Status == "awaiting");
    }

    static void SeedHildWake(out string armId)
    {
        armId = HildArmIdPrefix
            + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N")[..6];

        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1s"),
                ["once"] = JsonSerializer.SerializeToElement(true),
                ["charge"] = JsonSerializer.SerializeToElement("minimal"),
                ["task"] = JsonSerializer.SerializeToElement("HILD human_away"),
                ["id"] = JsonSerializer.SerializeToElement(armId),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(1)
            };
            _ = IdeIgniteChannel.HandleJson(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] hild seed wake failed: {ex.Message}");
        }
    }
}
