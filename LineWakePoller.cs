using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// ADR-0212 stage (d): LineWakePoller — the missing postman. Lives inside CdpService
/// (eternal host), polls %LocalAppData%/cdp-mcp/arms/line-*.json and delivers each wake
/// to its line: if the registry knows the opencode session, spawns
/// `opencode run --session=<id> "<body>"` (generation starts without the operator);
/// otherwise marks the note delivered-on-entry (the line reads it on next claim).
/// Consumed notes are moved to *.done — at-most-once delivery.
/// Echo-storm lessons (Света 2026-09-06): empty body / self-echo letters are archived
/// without delivery; deliveries are throttled (one per DeliveryCooldown) and the poll
/// is single-flight (reentrancy guard) — the ping-pong must not outrun the operator.
/// Emergency stop (Света): файл-флаг arms/poller.stop — пока существует, почтальон
/// молчит (ноты копятся не consumed и доставятся после снятия). Кнопка: cdp_intercom
/// op=poller action=stop|start|status — руками файл не трогать.
/// </summary>
internal sealed class LineWakePoller : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeliveryCooldown = TimeSpan.FromSeconds(15);
    private Timer? _timer;
    private readonly string _armsDir;
    private readonly string _doneDir;
    private static int _busy;
    private static DateTimeOffset _lastDeliveryUtc;

    public LineWakePoller()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");
        _armsDir = Path.Combine(root, "arms");
        _doneDir = Path.Combine(root, "arms", "done");
    }

    public static string StopFlagPath => Path.Combine(StateRootStatic, "arms", "poller.stop");

    private static string StateRootStatic => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp");

    /// <summary>Emergency Stop: создать флаг-файл — почтальон молчит.</summary>
    public static void StopSwitch()
    {
        var dir = Path.Combine(StateRootStatic, "arms");
        Directory.CreateDirectory(dir);
        File.WriteAllText(StopFlagPath,
            $"stopped_utc={DateTimeOffset.UtcNow:O}{Environment.NewLine}stopped_by=operator");
    }

    /// <summary>Снять Emergency Stop: удалить флаг-файл.</summary>
    public static void StartSwitch()
    {
        if (File.Exists(StopFlagPath))
            File.Delete(StopFlagPath);
    }

    public static bool IsStopped => File.Exists(StopFlagPath);

    public void Start()
    {
        Directory.CreateDirectory(_armsDir);
        Directory.CreateDirectory(_doneDir);
        _timer = new Timer(_ => _ = PollOnceAsync(), null, PollInterval, PollInterval);
    }

    public void Dispose() => _timer?.Dispose();

    internal async Task PollOnceAsync()
    {
        // Single-flight: overlapping ticks must not process the same note twice
        // (двойные доставки = дубли user-ходов в сессии линии).
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;
        try
        {
            await PollCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task PollCoreAsync()
    {
        // Emergency Stop: файл-флаг есть — почтальон полностью молчит,
        // ноты остаются pending (не consumed) и доставятся после снятия.
        if (IsStopped)
            return;

        string[] notes;
        try
        {
            notes = Directory.GetFiles(_armsDir, "line-*.json");
        }
        catch
        {
            return;
        }

        foreach (var notePath in notes)
        {
            try
            {
                var json = await File.ReadAllTextAsync(notePath).ConfigureAwait(false);
                var note = JsonSerializer.Deserialize<LineWakeNote>(json, Json);
                if (note is null || note.Consumed)
                {
                    Archive(notePath);
                    continue;
                }

                // Wake hygiene (Света 2026-09-06): пустое тело будит линию пустым
                // user-ходом, самостук (From == Nick) — самоэхо. Оба не доставляем:
                // архив молча, без opencode spawn.
                if (string.IsNullOrWhiteSpace(note.Body)
                    || (note.From is not null
                        && note.From.Equals(note.Nick, StringComparison.OrdinalIgnoreCase)))
                {
                    Archive(notePath);
                    continue;
                }

                var agent = CideIntercomAgents.Resolve(note.Nick);
                if (agent is null)
                {
                    // Line not registered (or de-registered) — keep the note for later.
                    continue;
                }

                if (agent.Harness.Equals("opencode", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(agent.Session))
                {
                    // Throttle (Света 2026-09-06): ping-pong линий не должен гонять
                    // почтальона без паузы — не чаще одной доставки за cooldown.
                    var nowUtc = DateTimeOffset.UtcNow;
                    if (nowUtc - _lastDeliveryUtc < DeliveryCooldown)
                        continue; // нота остаётся pending, прилетит на следующем тике
                    _lastDeliveryUtc = nowUtc;

                    var body = $"[intercom from {note.From}] {note.Body}";
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        // opencode is a node script (.cmd/.ps1) — Process.Start can't exec it
                        // directly; cmd /c resolves the PATHEXT chain for us.
                        FileName = "cmd.exe",
                        Arguments = $"/c opencode.cmd run --session={agent.Session} {Quote(body)}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    note.Consumed = true;
                }
                else
                {
                    // No session bound yet — delivered-on-entry: leave pending, the line
                    // reads it on next claim (registry Resolve keeps it visible).
                    note.DeliveredOnEntry = true;
                }

                File.WriteAllText(notePath, JsonSerializer.Serialize(note, Json));
                if (note.Consumed)
                    Archive(notePath);
            }
            catch
            {
                /* best effort — next poll retries */
            }
        }
    }

    private static void Archive(string notePath)
    {
        try
        {
            File.Move(notePath, Path.Combine(_doneDirStatic, Path.GetFileName(notePath)), overwrite: true);
        }
        catch
        {
            /* best effort */
        }
    }

    private static readonly string _doneDirStatic = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp", "arms", "done");

    private static string Quote(string arg) =>
        arg.Contains(' ') || arg.Contains('"')
            ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : arg;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public sealed class LineWakeNote
    {
        public string Schema { get; set; } = "intercom_line_wake/v1";
        public string Nick { get; set; } = "";
        public string? From { get; set; }
        public string? Channel { get; set; }
        public string Body { get; set; } = "";
        public DateTimeOffset StampedUtc { get; set; }
        public bool Consumed { get; set; }
        public bool DeliveredOnEntry { get; set; }
    }
}