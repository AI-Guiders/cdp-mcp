#nullable enable
using static CdpMcp.IdeIgniteArmHost;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// HILD — Human-in-the-loop detector via CDT Composer.

/// First away = partner status; still away after <see cref="AwayEscalateAfter"/> → autonomy.
/// </summary>
internal sealed partial class CdpIgniteArmHost
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

    readonly TimeSpan HildPollInterval = TimeSpan.FromSeconds(1);
    /// <summary>First away = status; still away after this → autonomy (partner likely gone long).</summary>
    internal TimeSpan AwayEscalateAfter { get; set; } = TimeSpan.FromSeconds(60);

    readonly IdeHildDetector HildDetector = new();
    readonly object HildGate = new();

    CancellationTokenSource? HildCts;
    int HildPort = IdeIgniteChannel.DefaultPort;
    bool HildArmed = true;
    bool? HildOverride;
    bool HildLoaded;
    IdeHildDetector.Status HildLastStatus = IdeHildDetector.Status.Idle;
    DateTimeOffset? HildLastEdgeUtc;
    string? HildLastSampleKind;
    int HildEdgeCount;
    DateTimeOffset? AwayEscalateDueUtc;
    bool AwayEscalateDone;

    public string HildStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        Seat switch
        {
            "cdp-debug" => "hild-cdp-debug.json",
            "cdp" => "hild-cdp.json",
            _ => "hild-other.json"
        });

    /// <summary>Tests: force armed without disk.</summary>
    internal void BindHild(bool? armed) => HildOverride = armed;

    /// <summary>Test hook — detector instance.</summary>
    internal IdeHildDetector HildDetectorForTests => HildDetector;

    public bool IsHildArmed()
    {
        if (HildOverride is { } o)
            return o;
        EnsureHildLoaded();
        lock (HildGate)
            return HildArmed;
    }

    int HildLoopStarted;

    void EnsureHildStarted()
    {
        EnsureHildLoaded();
        if (Interlocked.Exchange(ref HildLoopStarted, 1) != 0)
            return;

        var cts = new CancellationTokenSource();
        Volatile.Write(ref HildCts, cts);
        _ = Task.Run(() => HildLoopAsync(cts.Token));
    }

    /// <summary>Called from <see cref="EnsureStarted"/>.</summary>
    internal void StartHildWatch()
    {
        EnsureHildStarted();
    }

    public object Hild(IReadOnlyDictionary<string, JsonElement>? args = null)
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

    public object SetHild(bool armed, string? why = null)
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

        PublishGlass();
        return HildStatusPayload(why);
    }

    public object HildStatusPayload(string? why = null)
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
                
            };
        }
    }

    void EnsureHildLoaded()
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

    void PersistHildUnlocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HildStorePath)!);
            var json = JsonSerializer.Serialize(new
            {
                schema = HildStoreSchema,
                armed = HildArmed,
                updated_utc = _time.GetUtcNow()
            }, JsonOpts);
            File.WriteAllText(HildStorePath, json);
        }
        catch
        {
            /* best-effort */
        }
    }

    async Task HildLoopAsync(CancellationToken ct)
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

    async Task HildTickOnceAsync(CancellationToken ct)
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
                _time.GetUtcNow()));
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
