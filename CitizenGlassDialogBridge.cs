#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Glass → habitat citizen dialog bridge.
/// Request latch: %LocalAppData%/cdp-mcp/citizen-dialog-request-LATEST.json
/// Reply: Intercom voice latch as kind=citizen @PF → @PM (Glass journal watches).
/// </summary>
internal static class CitizenGlassDialogBridge
{
    public const string Schema = "citizen_dialog_request/v0";
    public const string FileName = "citizen-dialog-request-LATEST.json";

    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static CancellationTokenSource? Cts;
    static string? LastProcessedId;
    internal static string? RootOverrideForTests { get; set; }

    /// <summary>Test hook — when set, skips live CitizenCompletions.</summary>
    internal static Func<string, CitizenCompletions.TurnResult>? TurnOverrideForTests { get; set; }

    internal static void ResetProcessedForTests() => LastProcessedId = null;

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string RequestPath => Path.Combine(StateRoot, FileName);

    public static bool IsRunning =>
        Volatile.Read(ref Cts) is { IsCancellationRequested: false };

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

    /// <summary>Process pending request if any (also used by tests).</summary>
    public static bool TryProcessOnce()
    {
        var req = TryReadPending();
        if (req is null)
            return false;
        if (string.Equals(LastProcessedId, req.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        LastProcessedId = req.Id;
        MarkStatus(req, "running");

        try
        {
            CitizenCompletions.TurnResult turn;
            if (TurnOverrideForTests is { } ov)
            {
                turn = ov(req.Body);
            }
            else
            {
                var live = CitizenLiveDesk.TryCaptureLive();
                turn = CitizenCompletions.Turn(
                    req.Body,
                    boardLines: live.BoardLines.Length > 0 ? live.BoardLines : null,
                    tm: live.TmPulse,
                    inject: true,
                    mode: CitizenTurnMode.Dialog,
                    history: true);
            }

            if (!turn.Ok || string.IsNullOrWhiteSpace(turn.Text))
            {
                MarkStatus(req, "error", turn.Error ?? "empty_reply");
                return true;
            }

            IReadOnlyList<CitizenRouteHost.Applied>? executed = null;
            CitizenPeerAck.Result? peerAck = null;
            if (turn.Routes is { Count: > 0 })
            {
                executed = CitizenRouteHost.Execute(turn.Routes);
                peerAck = CitizenPeerAck.FromExecuted(executed);
            }

            // Human Intercom: strip wire; harness → «Сделала: …» (not peer tip dump).
            var publishBody = SurfacePublishBody(turn.Text!, executed);

            var published = CideIntercomVoiceLatch.Publish(
                fromSeat: CideIntercomVoiceLatch.SeatPf,
                toSeat: CideIntercomVoiceLatch.SeatPm,
                body: publishBody,
                origin: CideIntercomVoiceLatch.OriginAgent,
                id: null,
                name: CideIntercomVoiceLatch.DefaultNameCitizen,
                kind: CideIntercomVoiceLatch.KindCitizen);
            MarkStatus(
                req,
                published is null ? "error" : "done",
                published is null ? "publish_failed" : null,
                peer: peerAck?.Peer);
            return true;
        }
        catch (Exception ex)
        {
            MarkStatus(req, "error", ex.GetType().Name);
            return true;
        }
    }

    static string SurfacePublishBody(string prose, IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        var body = CitizenIntercomHumanSurface.Publish(prose, executed);
        // Dialog SA walls → Radio leaf pointer (I6), not @frame desk dump on Glass.
        if (CitizenIntercomHumanSurface.LooksLikeSaInstrumentWall(body) || body.Length > 480)
        {
            return IdeIgniteArmHost.FormatHabitatIntercomRadio(
                arm: null,
                charge: body);
        }

        return body;
    }

    static RequestDoc? TryReadPending()
    {
        try
        {
            if (!File.Exists(RequestPath))
                return null;
            var raw = File.ReadAllText(RequestPath);
            var doc = JsonSerializer.Deserialize<RequestDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.Body))
                return null;
            var status = doc.Status?.Trim().ToLowerInvariant() ?? "pending";
            if (status is "done" or "error" or "running")
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    static void MarkStatus(RequestDoc req, string status, string? error = null, string? peer = null)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            req.Status = status;
            req.Error = error;
            req.Peer = peer;
            req.ProcessedUtc = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(req, JsonOpts);
            var tmp = RequestPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, RequestPath, overwrite: true);
        }
        catch
        {
            /* ignore */
        }
    }


    internal sealed class RequestDoc
    {
        public string Schema { get; set; } = CitizenGlassDialogBridge.Schema;
        public string Id { get; set; } = "";
        public string Body { get; set; } = "";
        public string? Status { get; set; } = "pending";
        public string? Error { get; set; }
        public string? Peer { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
        public DateTimeOffset? ProcessedUtc { get; set; }
    }
}
