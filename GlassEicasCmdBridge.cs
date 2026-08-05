#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Glass EICAS SoftKeys → habitat ECL ack.
/// Request: %LocalAppData%/cdp-mcp/eicas-cmd-LATEST.json
/// </summary>
internal static class GlassEicasCmdBridge
{
    public const string Schema = "glass_eicas_cmd/v0";
    public const string FileName = "eicas-cmd-LATEST.json";

    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);
    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static CancellationTokenSource? Cts;
    static string? LastProcessedId;
    internal static string? RootOverrideForTests { get; set; }

    internal static void ResetProcessedForTests() => LastProcessedId = null;

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string RequestPath => Path.Combine(StateRoot, FileName);

    public static void Start()
    {
        Stop();
        var cts = new CancellationTokenSource();
        Volatile.Write(ref Cts, cts);
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    public static void Stop()
    {
        var cts = Interlocked.Exchange(ref Cts, null);
        if (cts is null)
            return;
        try
        {
            cts.Cancel();
            cts.Dispose();
        }
        catch
        {
            /* ignore */
        }
    }

    static async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TryProcessOnce();
            }
            catch
            {
                /* best-effort */
            }

            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal static bool TryProcessOnce()
    {
        if (!File.Exists(RequestPath))
            return false;

        CmdDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<CmdDoc>(File.ReadAllText(RequestPath), ReadOpts);
        }
        catch
        {
            return false;
        }

        if (doc is null
            || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(doc.Id)
            || string.IsNullOrWhiteSpace(doc.Op))
            return false;

        if (string.Equals(doc.Status, "done", StringComparison.OrdinalIgnoreCase)
            || string.Equals(doc.Status, "error", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(LastProcessedId, doc.Id, StringComparison.Ordinal))
            return false;

        LastProcessedId = doc.Id;
        try
        {
            ApplyOp(doc);
            WriteStatus(doc, "done", null);
            return true;
        }
        catch (Exception ex)
        {
            WriteStatus(doc, "error", ex.Message);
            return true;
        }
    }

    static void ApplyOp(CmdDoc doc)
    {
        switch (doc.Op!.Trim().ToLowerInvariant())
        {
            case "ack_ecl":
            {
                var checklist = doc.Checklist;
                var item = doc.Item;
                if (string.IsNullOrWhiteSpace(checklist) || string.IsNullOrWhiteSpace(item))
                {
                    var latch = CideEclLatch.TryRead();
                    checklist ??= latch?.HotId;
                    item ??= latch?.OpenItems?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Id))?.Id;
                }

                if (string.IsNullOrWhiteSpace(checklist) || string.IsNullOrWhiteSpace(item))
                    throw new InvalidOperationException("ack_ecl needs checklist+item");

                IdeChkChannel.AckFromGlass(checklist!, item!);
                // Refresh Glass face from current acks (cheap ProbeCtx — ship checklist links).
                var ctx = new IdeChkChannel.ProbeCtx(
                    ProjectOpen: true,
                    TaskOpen: true,
                    IgniteIdle: false,
                    GitKnown: true,
                    GitDirty: true,
                    TestsGreen: true,
                    TestsFailed: false,
                    ProblemsClean: true,
                    DapStopped: false,
                    DapActive: false,
                    SniperOk: true,
                    Phase: "act",
                    Intent: null);
                var snap = IdeChkChannel.Build(ctx);
                CideEclLatch.Publish(snap);
                break;
            }
            default:
                throw new InvalidOperationException("unknown_op:" + doc.Op);
        }
    }

    static void WriteStatus(CmdDoc src, string status, string? error)
    {
        try
        {
            var outDoc = new CmdDoc
            {
                Schema = Schema,
                Origin = src.Origin ?? "glass",
                Id = src.Id,
                Op = src.Op,
                Checklist = src.Checklist,
                Item = src.Item,
                Status = status,
                Error = error,
                StampedUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(outDoc, WriteOpts);
            var tmp = RequestPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, RequestPath, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    public sealed class CmdDoc
    {
        public string? Schema { get; set; }
        public string? Origin { get; set; }
        public string? Id { get; set; }
        public string? Op { get; set; }
        public string? Checklist { get; set; }
        public string? Item { get; set; }
        public string? Status { get; set; }
        public string? Error { get; set; }

        [JsonPropertyName("stamped_utc")]
        public DateTimeOffset StampedUtc { get; set; }
    }
}
