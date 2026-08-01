#nullable enable
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
    /// <summary>Stable first-away wake id — replaces prior GUID storm under zombie remounts.</summary>
    public const string HildAwayArmId = "hild-away";
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

    static int HildLoopStarted;

    static void EnsureHildStarted()
    {
        EnsureHildLoaded();
        if (Interlocked.Exchange(ref HildLoopStarted, 1) != 0)
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
}
