namespace CdpMcp;

/// <summary>
/// ADR-0212 stage (d) → ADR-0213: LineWakePoller теперь тонкий тик WakeDispatcher
/// (MessageBroker): таймер 5с → CideWakeDispatch.TickAsync. Очередь/подписки/состояние —
/// один SSOT (wake-dispatch.json); продюсеры кладут в очередь; каналы — тонкие адаптеры.
/// Emergency Stop — состояние в SSOT (не файл-флаг); legacy arms/ конвертируются диспетчером.
/// </summary>
internal sealed class LineWakePoller : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private Timer? _timer;

    public LineWakePoller()
    {
    }

    /// <summary>SSOT-стор диспетчера (кнопка op=poller показывает его путь).</summary>
    public static string StopFlagPath => CideWakeDispatch.StorePath;

    public static void StopSwitch() => CideWakeDispatch.SetStopped(true);

    public static void StartSwitch() => CideWakeDispatch.SetStopped(false);

    public static bool IsStopped => CideWakeDispatch.Stopped;

    public void Start()
    {
        _timer = new Timer(
            _ => _ = CideWakeDispatch.TickAsync(CancellationToken.None),
            null, PollInterval, PollInterval);
    }

    public void Dispose() => _timer?.Dispose();
}