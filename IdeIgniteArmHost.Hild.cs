#nullable enable
using System.Globalization;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// HILD — Human-in-the-loop detector via CDT Composer.
/// Voice/empty + no Composer text for 5s → <c>human_away</c> once → AutoIgnition wake.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    public const string HildArmIdPrefix = "hild-away-";
    public const string HildStoreSchema = "hild/v0";

    static readonly TimeSpan HildPollInterval = TimeSpan.FromSeconds(1);
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
                fired_this_spell = HildDetector.FiredThisSpell,
                why,
                hint = "Composer text resets 5s watch; Voice/empty → human_away once → AutoI. op=hild armed=false to disarm."
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

        IdeHildDetector.TickResult tick;
        lock (HildGate)
        {
            HildLastSampleKind = sample.Kind;
            tick = HildDetector.Tick(new IdeHildDetector.Sample(
                sample.Kind,
                sample.Text,
                DateTimeOffset.UtcNow));
            HildLastStatus = tick.Status;
        }

        if (!tick.EdgeHumanAway)
            return;

        OnHumanAwayEdge();
    }

    static void OnHumanAwayEdge()
    {
        lock (HildGate)
        {
            HildLastEdgeUtc = DateTimeOffset.UtcNow;
            HildEdgeCount++;
        }

        Console.Error.WriteLine(
            $"[ide_ignite] hild human_away edge #{HildEdgeCount} — notify + wake");

        // Event arms first (when=human_away).
        Notify("human_away", ok: true, pulse: "hild", detail: "composer_idle_5s");

        // Hard stop: explicit await_operator latch — do not steal the loop.
        if (HasAwaitingOperatorLatch())
        {
            Console.Error.WriteLine("[ide_ignite] hild wake suppressed — await_operator latch");
            return;
        }

        // Default wake (Intercom cannon pattern) — minimal charge.
        SeedHildWake();
    }

    static bool HasAwaitingOperatorLatch()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a => a.Status == "awaiting");
    }

    static void SeedHildWake()
    {
        var id = HildArmIdPrefix
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
                ["id"] = JsonSerializer.SerializeToElement(id),
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
