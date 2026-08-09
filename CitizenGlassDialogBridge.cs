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
    /// Same-turn observe nudge after host-execute — Face must see @event peer pulse
    /// in-loop (Cursor Cutoff densest), not sleep until next Autoi.
    /// SoftFL densify 2026-08-09b: SoftFL leaf known → continue (partner «меняй» ≠ wait vector);
    /// invent refuse stays; «жду вектора» teaching removed (anti-agency overshoot).
    /// </summary>
    internal const string SameTurnObserveUser =
        "@event peer — verify hands from pulse; do not invent refuse. "
        + "If SoftFL leaf is known (PASTE in charge / live SoftFL): continue that leaf — partner approve ≠ wait vector. "
        + "If truly no leaf named: one Radio fact OK; do not invent take path; find≠fabricate next. "
        + "Radio alone ≠ leaf progress when PASTE leaf is known.";

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
            if (status is not ("running" or "reconnecting"))
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
        CideHandsLatch.PublishRunning();
        var (who, kind) = ResolveCitizenFace();
        // ttl=0: hold busy until finally idle — DefaultBusyTtl (120s) went stale mid-Turn
        // → IsHabitatPartnerLive false → Autoi Radio tips during Sierra Completions (lived 2026-08-09).
        CideIntercomPresenceLatch.PublishSeat(
            CideIntercomVoiceLatch.SeatPf,
            CideIntercomPresenceLatch.StateBusy,
            ttlSeconds: 0,
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
                var prevHook = CitizenCompletions.TransientRetryHook;
                CitizenCompletions.TransientRetryHook = (attempt, max, err) =>
                    MarkStatus(
                        req,
                        "reconnecting",
                        "reconnecting " + attempt + "/" + max
                            + (string.IsNullOrWhiteSpace(err) ? "" : " · " + err));
                try
                {
                    turn = CitizenCompletions.Turn(
                        req.Body,
                        boardLines: live.BoardLines.Length > 0 ? live.BoardLines : null,
                        tm: live.TmPulse,
                        inject: true,
                        mode: CitizenTurnMode.Dialog,
                        history: true,
                        appendHistory: false);
                }
                finally
                {
                    CitizenCompletions.TransientRetryHook = prevHook;
                }
            }

            if (!turn.Ok || string.IsNullOrWhiteSpace(turn.Text))
            {
                CideHandsLatch.Clear();
                MarkStatus(req, "error", turn.Error ?? "empty_reply");
                return true;
            }

            // Provider may invent @frame/prose without @intent (Glass Face seeming) —
            // host-execute user @intent when model routes empty (parity with IdeCitizenChannel).
            IReadOnlyList<CitizenIntentRouter.Route>? routes = turn.Routes;
            if (routes is null || routes.Count == 0)
            {
                var userRoutes = CitizenIntentRouter.RouteAll(CitizenWireParser.Parse(req.Body));
                if (userRoutes.Count > 0)
                    routes = userRoutes;
            }

            // SoftFL lived 2026-08-08: FM invents «Сделала: find …» without @intent →
            // Radio seems hands-ran, peer latch stale, no peer_ready wake.
            if ((routes is null || routes.Count == 0)
                && CitizenInventedHands.LooksLikeHandsClaim(turn.Text))
            {
                var recovered = CitizenInventedHands.TryRecoverRoutes(turn.Text);
                if (recovered.Count > 0)
                    routes = recovered;
            }

            IReadOnlyList<CitizenRouteHost.Applied>? executed = null;
            CitizenPeerAck.Result? peerAck = null;
            if (routes is { Count: > 0 })
            {
                executed = CitizenRouteHost.Execute(routes);
                peerAck = CitizenPeerAck.FromExecuted(executed);
            }

            // Act letter first (dialog memory), then same-turn observe if hands ran.
            // SoftOrgan HND owns receipt chips; letter stays prose (no FormatHands laundry).
            var elapsed = DateTimeOffset.UtcNow - req.StampedUtc;
            CideHandsLatch.PublishDone(executed, elapsed);
            var actPublished = SurfacePublishBody(turn.Text!, executed, elapsed);
            PersistOperatorDialog(req.Body, turn.Text!, actPublished, executed);

            // Face letter prefers same-turn observe; thin observe ("R" / 1-token) falls back to act.
            var publishBody = actPublished;
            var sameTurnObserveRan = false;
            if (peerAck is not null)
            {
                var observe = TrySameTurnObserve(peerAck, executed);
                if (observe is { } obs)
                {
                    sameTurnObserveRan = true;
                    if (IsUsableFaceLetter(obs.PublishBody))
                    {
                        publishBody = obs.PublishBody;
                        peerAck = obs.PeerAck;
                        PersistOperatorDialog(
                            SameTurnObserveUser,
                            obs.Text,
                            obs.PublishBody,
                            obs.Executed);
                    }
                    else
                    {
                        // Keep act letter on Face; still persist observe dig for dialog memory.
                        PersistOperatorDialog(
                            SameTurnObserveUser,
                            obs.Text,
                            actPublished,
                            obs.Executed ?? executed);
                        if (obs.PeerAck is not null)
                            peerAck = obs.PeerAck;
                    }
                }
            }

            // SoftOrgan HND owns receipt chips — wire-only Completions strip to empty →
            // Publish null → publish_failed → busy→idle with no Radio (lived @Sierra paste).
            if (string.IsNullOrWhiteSpace(publishBody))
                publishBody = FaceLetterFallback(turn.Text!, executed, peerAck);

            // Human eyes: SoftOrgan chip ≠ journal. Ship body (files listing / take text)
            // must reach Face letter — pulse "files · 37" alone is seeming (lived 2026-08-09).
            publishBody = AppendShipForHumanFace(publishBody, executed);

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
            {
                LastProcessedId = req.Id;
                // Result-wake facade: observe Completions #2 ≠ stop — AfterHands arms peer_ready for #3.
                CitizenResultWake.AfterHands(
                    peerAck,
                    req.Channel,
                    req.Body,
                    sameTurnObserveRan: sameTurnObserveRan);
            }
            return true;
        }
        catch (Exception ex)
        {
            CideHandsLatch.Clear();
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
    /// Lived 2026-08-06: FM observe collapsed to "R" after StripWire — Face useless.
    /// Prefer act letter when observe publish is thinner than a short Radio sentence.
    /// </summary>
    internal static bool IsUsableFaceLetter(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        var t = body.Trim();
        if (t.Length < 24)
            return false;
        // Single token / glyph ("R", "ok") ≠ human-faced observe letter.
        if (t.IndexOfAny([' ', '\n', '\t', '·', '.', ',', '—', '-']) < 0 && t.Length < 40)
            return false;
        return true;
    }

    /// <summary>
    /// Never leave Radio empty after a citizen turn — SoftOrgan chips ≠ journal letter.
    /// </summary>
    internal static string FaceLetterFallback(
        string prose,
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        CitizenPeerAck.Result? peerAck)
    {
        if (executed is { Count: > 0 })
        {
            var fails = executed
                .Where(a => !a.Ok)
                .Select(a => a.Pulse ?? a.Reason)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(2)
                .ToArray();
            if (fails.Length > 0)
                return "Hands FAIL · " + string.Join(" · ", fails!);

            var ships = executed
                .Where(IsHumanFaceListingShip)
                .Select(a => TruncFaceShip(a.Ship!))
                .Take(2)
                .ToArray();
            if (ships.Length > 0)
            {
                var pulseTip = executed
                    .Where(a => a.Ok)
                    .Select(a => a.Pulse)
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                var head = string.IsNullOrWhiteSpace(pulseTip)
                    ? "Hands ok"
                    : "Hands ok · " + pulseTip;
                return head + "\n\n" + string.Join("\n\n", ships!);
            }

            var oks = executed
                .Where(a => a.Ok)
                .Select(a => a.Pulse)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(2)
                .ToArray();
            if (oks.Length > 0)
                return "Hands ok · " + string.Join(" · ", oks!) + " — продолжаю после dig.";

            // Hands ran (SoftOrgan chip) but pulse thin — still a human Face letter.
            return executed.Any(a => !a.Ok)
                ? "Hands FAIL · детали в SoftOrgan HND."
                : "Hands ok · продолжаю после dig.";
        }

        if (peerAck?.Peer is { Length: > 0 } peer)
        {
            var tip = peer.Length <= 280 ? peer : peer[..279] + "…";
            return "Peer: " + tip;
        }

        var stripped = CitizenIntercomHumanSurface.StripWire(prose);
        if (!string.IsNullOrWhiteSpace(stripped))
            return stripped;

        return "Ход без Radio-письма (wire/empty). Смотри SoftOrgan HND; повтори с короткой прозой.";
    }

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
        /// <summary>
    /// Face Who for busy cue + Radio: Activate tip for live model slot.
    /// Missing / guest / operator tip → bootstrap Citizen (Cursor Who ≠ habitat Face).
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
            var kindNorm = CideIntercomVoiceLatch.NormalizeKind(kind)
                ?? CideIntercomVoiceLatch.KindCitizen;
            // SoftFL: Cursor PF Who (guest|operator) must not stomp Glass citizen Face/Radio.
            if (string.Equals(kindNorm, CideIntercomVoiceLatch.KindGuest, StringComparison.Ordinal)
                || string.Equals(kindNorm, CideIntercomVoiceLatch.KindOperator, StringComparison.Ordinal))
                return (CideIntercomVoiceLatch.DefaultNameCitizen, CideIntercomVoiceLatch.KindCitizen);
            return (seat.Name.Trim(), CideIntercomVoiceLatch.KindCitizen);
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

    internal const int FaceShipMaxChars = 2_400;

    /// <summary>
    /// SoftOrgan HND owns receipt chrome; short directory listings may hit Face.
    /// Never dump take/body walls (lived 0.5.692 csharp thrash).
    /// </summary>
    internal static string AppendShipForHumanFace(
        string? letter,
        IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        var ships = executed?
            .Where(IsHumanFaceListingShip)
            .Select(a => TruncFaceShip(a.Ship!))
            .Take(2)
            .ToArray();
        if (ships is not { Length: > 0 })
            return letter?.Trim() ?? "";

        var block = string.Join("\n\n", ships!);
        if (string.IsNullOrWhiteSpace(letter))
            return block;

        var tip = letter.Trim();
        // Already carried (fallback path or model quoted listing).
        var probeLen = Math.Min(48, block.Length);
        if (probeLen > 0 && tip.Contains(block[..probeLen], StringComparison.Ordinal))
            return tip;
        return tip + "\n\n" + block;
    }

    /// <summary>files Ship / cwd|dir|file board — not take csharp walls.</summary>
    internal static bool IsHumanFaceListingShip(CitizenRouteHost.Applied a)
    {
        if (!a.Ok || string.IsNullOrWhiteSpace(a.Ship))
            return false;
        if (string.Equals(a.Action, "files", StringComparison.OrdinalIgnoreCase))
            return true;
        return LooksLikeDirectoryListingShip(a.Ship!);
    }

    internal static bool LooksLikeDirectoryListingShip(string ship)
    {
        var t = ship.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart();
        if (t.Length == 0)
            return false;
        // take / buffer walls — never Face dump
        if (t.Contains("namespace ", StringComparison.Ordinal)
            || t.Contains("#nullable", StringComparison.Ordinal)
            || (t.Contains('{') && t.Contains('}') && t.Length > 400))
            return false;
        if (t.StartsWith("cwd |", StringComparison.Ordinal))
            return true;
        foreach (var line in t.Split('\n'))
        {
            var s = line.TrimStart();
            if (s.StartsWith("dir ", StringComparison.Ordinal)
                || s.StartsWith("file ", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static string TruncFaceShip(string ship)
    {
        ship = ship.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        if (ship.Length <= FaceShipMaxChars)
            return ship;
        return ship[..FaceShipMaxChars] + "\n…";
    }

    static string SurfacePublishBody(
        string prose,
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        TimeSpan? elapsed = null)
    {
        var body = CitizenIntercomHumanSurface.Publish(prose, executed, elapsed);
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
            if (status is "done" or "error" or "running" or "reconnecting")
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
