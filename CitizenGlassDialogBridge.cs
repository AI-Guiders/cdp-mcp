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
        if (!IdeIgniteArmHost.IsPrimaryAutoiSeat())
            return;
        Stop();
        RecoverOrphanRunning();
        var cts = new CancellationTokenSource();
        Volatile.Write(ref Cts, cts);
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    /// <summary>
    /// Remount/KillRunning mid-Turn leaves status=running forever (TryReadPending skips it).
    /// On bridge Start, orphan running → pending and clear stuck pf busy.
    /// </summary>
    internal static void RecoverOrphanRunning()
    {
        try
        {
            if (!File.Exists(RequestPath))
                return;
            var raw = File.ReadAllText(RequestPath);
            var doc = JsonSerializer.Deserialize<RequestDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return;
            var status = doc.Status?.Trim().ToLowerInvariant() ?? "";
            if (status is not "running")
                return;

            MarkStatus(doc, "pending");
            CideIntercomPresenceLatch.PublishSeat(
                CideIntercomVoiceLatch.SeatPf,
                CideIntercomPresenceLatch.StateIdle);
        }
        catch
        {
            /* best-effort */
        }
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
        if (!IdeIgniteArmHost.IsPrimaryAutoiSeat())
            return false;

        var req = TryReadPending();
        if (req is null)
            return false;
        if (string.Equals(LastProcessedId, req.Id, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IdeIgniteArmHost.TryClaimSharedWakeMirror("citizen-bridge:" + req.Id))
            return false;

        MarkStatus(req, "running");
        CideIntercomPresenceLatch.PublishSeat(
            CideIntercomVoiceLatch.SeatPf,
            CideIntercomPresenceLatch.StateBusy,
            ttlSeconds: CideIntercomPresenceLatch.DefaultBusyTtlSeconds,
            who: CideIntercomVoiceLatch.DefaultNameCitizen,
            kind: CideIntercomVoiceLatch.KindCitizen);
        // Face roster Who during Turn — remount AutoI sticky must not own the cue.
        CideIntercomIdentityLatch.Claim(
            CideIntercomVoiceLatch.SeatPf,
            CideIntercomVoiceLatch.DefaultNameCitizen,
            CideIntercomVoiceLatch.KindCitizen);

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
                    history: true,
                    appendHistory: false);
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
            // Dialog memory wants human prose — not Autoi Radio collapse of SA walls.
            PersistOperatorDialog(req.Body, turn.Text!, publishBody, executed);

            var radioPointer = IdeIgniteArmHost.LooksLikeHabitatRadioPointer(publishBody);
            var published = CideIntercomVoiceLatch.Publish(
                fromSeat: CideIntercomVoiceLatch.SeatPf,
                toSeat: CideIntercomVoiceLatch.SeatPm,
                body: publishBody,
                origin: CideIntercomVoiceLatch.OriginAgent,
                id: null,
                name: radioPointer ? "AutoI" : CideIntercomVoiceLatch.DefaultNameCitizen,
                kind: radioPointer ? "guest" : CideIntercomVoiceLatch.KindCitizen,
                channel: ResolveRequestChannel(req));
            MarkStatus(
                req,
                published is null ? "error" : "done",
                published is null ? "publish_failed" : null,
                peer: peerAck?.Peer);
            if (published is not null)
                LastProcessedId = req.Id;
            return true;
        }
        catch (Exception ex)
        {
            MarkStatus(req, "error", ex.GetType().Name);
            return true;
        }
        finally
        {
            CideIntercomPresenceLatch.PublishSeat(
                CideIntercomVoiceLatch.SeatPf,
                CideIntercomPresenceLatch.StateIdle);
        }
    }

    /// <summary>Glass CIT operator thread — human prose (+ hands) for multi-turn memory, not raw wire.</summary>
    internal static void PersistOperatorDialog(
        string userBody,
        string prose,
        string publishBody,
        IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        var assistant = publishBody.Trim();
        if (IdeIgniteArmHost.LooksLikeHabitatRadioPointer(assistant))
            assistant = "";
        if (string.IsNullOrWhiteSpace(assistant))
            assistant = CitizenIntercomHumanSurface.Publish(prose, executed);
        if (string.IsNullOrWhiteSpace(assistant))
            assistant = CitizenIntercomHumanSurface.StripWire(prose);
        if (string.IsNullOrWhiteSpace(assistant))
            assistant = prose.Trim();
        if (string.IsNullOrWhiteSpace(assistant))
            return;
        CitizenDialogHistory.Append(userBody, assistant);
    }

    static string SurfacePublishBody(string prose, IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        var body = CitizenIntercomHumanSurface.Publish(prose, executed);
        // @frame desk SA walls → Radio leaf pointer (I6). Dialog prose stays on operator channel (#crew).
        if (CitizenIntercomHumanSurface.LooksLikeSaInstrumentWall(body))
        {
            return IdeIgniteArmHost.FormatHabitatIntercomRadio(
                arm: null,
                charge: body);
        }

        return body;
    }

    static string ResolveRequestChannel(RequestDoc req)
    {
        var raw = req.Channel?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return "crew";
        return raw.ToLowerInvariant() switch
        {
            "crew" or "#crew" => "crew",
            "radio" => "radio",
            "dm" or "direct" or "1:1" => "dm",
            _ => "crew"
        };
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
        /// <summary>NorthStar feed tag from Glass Send (crew | radio | dm).</summary>
        public string? Channel { get; set; }
        public string? Status { get; set; } = "pending";
        public string? Error { get; set; }
        public string? Peer { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
        public DateTimeOffset? ProcessedUtc { get; set; }
    }
}
