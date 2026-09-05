using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// ADR-0212 stage (d): LineWakePoller — the missing postman. Lives inside CdpService
/// (eternal host), polls %LocalAppData%/cdp-mcp/arms/line-*.json and delivers each wake
/// to its line: if the registry knows the opencode session, spawns
/// `opencode run --session=<id> "<body>"` (generation starts without the operator);
/// otherwise marks the note delivered-on-entry (the line reads it on next claim).
/// Consumed notes are moved to *.done — at-most-once delivery.
/// </summary>
internal sealed class LineWakePoller : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private Timer? _timer;
    private readonly string _armsDir;
    private readonly string _doneDir;

    public LineWakePoller()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");
        _armsDir = Path.Combine(root, "arms");
        _doneDir = Path.Combine(root, "arms", "done");
    }

    public void Start()
    {
        Directory.CreateDirectory(_armsDir);
        Directory.CreateDirectory(_doneDir);
        _timer = new Timer(_ => _ = PollOnceAsync(), null, PollInterval, PollInterval);
    }

    public void Dispose() => _timer?.Dispose();

    internal async Task PollOnceAsync()
    {
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

                var agent = CideIntercomAgents.Resolve(note.Nick);
                if (agent is null)
                {
                    // Line not registered (or de-registered) — keep the note for later.
                    continue;
                }

                if (agent.Harness.Equals("opencode", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(agent.Session))
                {
                    var body = $"[intercom from {note.From}] {note.Body}";
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        // opencode is a node script (.cmd/.ps1) — Process.Start can't exec it
                        // directly; cmd /c resolves the PATHEXT chain for us.
                        FileName = "cmd.exe",
                        Arguments = $"/c opencode run --session={agent.Session} {Quote(body)}",
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
