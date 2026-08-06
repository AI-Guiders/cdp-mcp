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

    /// <summary>
    /// Same-turn observe nudge after host-execute — Sierra must see @event peer pulse
    /// in-loop (Cursor Cutoff densest), not sleep until next Autoi.
    /// </summary>
    internal const string SameTurnObserveUser =
        "@event peer — verify hands from pulse; do not invent refuse. One short Radio letter.";

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
        var (who, kind) = ResolveCitizenFace();
        CideIntercomPresenceLatch.PublishSeat(
            CideIntercomVoiceLatch.SeatPf,
            CideIntercomPresenceLatch.StateBusy,
            ttlSeconds: CideIntercomPresenceLatch.DefaultBusyTtlSeconds,
            who: who,
            kind: kind);
        // Sticky Who wins — never Claim DefaultNameCitizen (stomps Sierra).

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

            // Act letter first (dialog memory), then same-turn observe if hands ran.
            var actPublished = SurfacePublishBody(turn.Text!, executed);
            PersistOperatorDialog(req.Body, turn.Text!, actPublished, executed);

            var publishBody = actPublished;
            if (peerAck is not null)
            {
                var observe = TrySameTurnObserve(peerAck, executed);
                if (observe is { } obs)
                {
                    publishBody = obs.PublishBody;
                    peerAck = obs.PeerAck;
                    PersistOperatorDialog(
                        SameTurnObserveUser,
                        obs.Text,
                        obs.PublishBody,
                        obs.Executed);
                }
            }

            var radioPointer = IdeIgniteArmHost.LooksLikeHabitatRadioPointer(publishBody);
            var published = CideIntercomVoiceLatch.Publish(
                fromSeat: CideIntercomVoiceLatch.SeatPf,
                toSeat: CideIntercomVoiceLatch.SeatPm,
                body: publishBody,
                origin: CideIntercomVoiceLatch.OriginAgent,
                id: null,
                name: radioPointer ? "AutoI" : who,
                kind: radioPointer ? "guest" : kind,
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

    sealed record SameTurnObserve(
        string Text,
        string PublishBody,
        CitizenPeerAck.Result PeerAck,
        IReadOnlyList<CitizenRouteHost.Applied>? Executed);

    /// <summary>
    /// After host-execute: second Turn so Completions injects @event peer (LastEvent).
    /// Face letter = observe reply. Observe routes execute at most once (no third turn).
    /// </summary>
    static SameTurnObserve? TrySameTurnObserve(
        CitizenPeerAck.Result peerAck,
        IReadOnlyList<CitizenRouteHost.Applied>? priorExecuted)
    {
        CitizenCompletions.TurnResult observeTurn;
        try
        {
            observeTurn = TurnOverrideForTests is { } obsOv
                ? obsOv(SameTurnObserveUser)
                : CitizenCompletions.Turn(SameTurnObserveUser);
        }
        catch
        {
            return null;
        }

        if (!observeTurn.Ok || string.IsNullOrWhiteSpace(observeTurn.Text))
            return null;

        IReadOnlyList<CitizenRouteHost.Applied>? observeExecuted = null;
        var observeAck = peerAck;
        if (observeTurn.Routes is { Count: > 0 })
        {
            observeExecuted = CitizenRouteHost.Execute(observeTurn.Routes);
            observeAck = CitizenPeerAck.FromExecuted(observeExecuted) ?? peerAck;
        }
        else
        {
            // Keep act-turn peer latch; Completions already saw LastEvent for this Turn.
            _ = priorExecuted;
        }

        var publish = SurfacePublishBody(observeTurn.Text!, observeExecuted);
        return new SameTurnObserve(observeTurn.Text!, publish, observeAck, observeExecuted);
    }

    /// <summary>
    /// Face Who for busy cue + Radio: Activate tip for live model slot.
    /// Missing profile → bootstrap Citizen (do not inherit other model's Who).
    /// </summary>
    internal static (string Who, string Kind) ResolveCitizenFace()
    {
        var model = CitizenIdentity.ResolveCitizenModel();
        CitizenDialogHistory.ActiveModel = model;
        var seat = CideIntercomIdentityLatch.Activate(CideIntercomVoiceLatch.SeatPf, model);
        if (seat is { Name.Length: > 0 }
            && !CideIntercomVoiceLatch.IsSystemVoiceWho(seat.Name))
        {
            var kind = string.IsNullOrWhiteSpace(seat.Kind)
                ? CideIntercomVoiceLatch.KindCitizen
                : seat.Kind!;
            return (seat.Name.Trim(), kind);
        }

        return (CideIntercomVoiceLatch.DefaultNameCitizen, CideIntercomVoiceLatch.KindCitizen);
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
