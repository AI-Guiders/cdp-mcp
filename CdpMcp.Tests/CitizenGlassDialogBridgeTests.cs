#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenGlassDialogBridgeTests : IDisposable
{
    readonly string _root;

    public CitizenGlassDialogBridgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-citizen-glass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CitizenGlassDialogBridge.RootOverrideForTests = _root;
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
        CideIntercomPresenceLatch.RootOverrideForTests = _root;
        CitizenGlassDialogBridge.TurnOverrideForTests = body => EchoTurn(body);
        IdeIgniteArmHost.BindPrimaryAutoiSeat(true);
        CitizenGlassDialogBridge.Stop();
        CitizenGlassDialogBridge.ResetProcessedForTests();
        CitizenPeerAck.ResetForTests();
    }

    public void Dispose()
    {
        CitizenGlassDialogBridge.Stop();
        CitizenGlassDialogBridge.TurnOverrideForTests = null;
        CitizenGlassDialogBridge.RootOverrideForTests = null;
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        CideIntercomPresenceLatch.RootOverrideForTests = null;
        IdeIgniteArmHost.BindPrimaryAutoiSeat(null);
        CitizenPeerAck.ResetForTests();
        CitizenDialogHistory.ResetForTests();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void TryProcessOnce_pending_publishes_citizen_intercom_and_marks_done()
    {
        var id = "abc123def456"[..12];
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body = "привет citizen",
            status = "pending",
            stamped_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        using var latch = JsonDocument.Parse(File.ReadAllText(CideIntercomVoiceLatch.LatchPath));
        Assert.Equal("citizen-echo:привет citizen", latch.RootElement.GetProperty("body").GetString());
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, latch.RootElement.GetProperty("kind").GetString());
        Assert.Equal(CideIntercomVoiceLatch.DefaultNameCitizen, latch.RootElement.GetProperty("name").GetString());
        Assert.Equal("pf", latch.RootElement.GetProperty("from_seat").GetString());
        Assert.Equal("pm", latch.RootElement.GetProperty("to_seat").GetString());

        using var status = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("done", status.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void TryProcessOnce_marks_pf_busy_during_turn_then_idle()
    {
        string? midState = null;
        string? midWho = null;
        CitizenGlassDialogBridge.TurnOverrideForTests = body =>
        {
            var doc = CideIntercomPresenceLatch.TryReadEffective();
            midState = doc?.Pf?.State;
            midWho = doc?.Pf?.Who;
            return EchoTurn(body);
        };

        WritePending("busyid000001", "typing please");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());
        Assert.Equal(CideIntercomPresenceLatch.StateBusy, midState);
        Assert.Equal(CideIntercomVoiceLatch.DefaultNameCitizen, midWho);

        var after = CideIntercomPresenceLatch.TryReadEffective();
        Assert.Equal(CideIntercomPresenceLatch.StateIdle, after?.Pf?.State);
        Assert.Null(after?.Pf?.Who);
    }

    [Fact]
    public void RecoverOrphanRunning_resets_running_to_pending_and_clears_busy()
    {
        var forced = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id = "orphan000001",
            body = "stuck mid remount",
            status = "running",
            stamped_utc = DateTimeOffset.UtcNow.AddMinutes(-2),
            processed_utc = DateTimeOffset.UtcNow.AddMinutes(-2)
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(forced, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        Assert.NotNull(CideIntercomPresenceLatch.PublishSeat("pf", "busy", who: "Citizen", kind: "citizen"));

        CitizenGlassDialogBridge.RecoverOrphanRunning();

        using var status = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", status.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            CideIntercomPresenceLatch.StateIdle,
            CideIntercomPresenceLatch.TryReadEffective()?.Pf?.State);
    }

    [Fact]
    public void TryProcessOnce_skips_done_and_duplicate_id()
    {
        var id = "dupid0000001";
        WritePending(id, "once");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());
        Assert.False(CitizenGlassDialogBridge.TryProcessOnce());

        WritePending(id, "again-same-id", resetProcessed: false);
        Assert.False(CitizenGlassDialogBridge.TryProcessOnce());
    }

    [Fact]
    public void TryProcessOnce_turn_fail_marks_error_without_unread_for_pf()
    {
        CitizenGlassDialogBridge.TurnOverrideForTests = _ => new CitizenCompletions.TurnResult(
            Ok: false,
            Error: "boom",
            Hint: null,
            Text: null,
            Model: null,
            Provider: null,
            Built: null,
            WireIntents: null,
            Routes: null,
            DryRun: false);
        WritePending("errid0000001", "nope");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        using var status = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("error", status.RootElement.GetProperty("status").GetString());
        Assert.Equal("boom", status.RootElement.GetProperty("error").GetString());
        Assert.Null(CideIntercomVoiceLatch.TryUnreadForPf());
    }

    [Fact]
    public void TryProcessOnce_executes_routes_and_latches_peer_ack()
    {
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");

        CitizenGlassDialogBridge.TurnOverrideForTests = body => EchoTurn(
            body,
            routes: [CitizenIntentRouter.RouteOne("go=plan")]);
        WritePending("routeid00001", "hands please");

        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        Assert.False(string.IsNullOrWhiteSpace(CitizenPeerAck.LastPeer));
        Assert.Contains("ack=1/1", CitizenPeerAck.LastPeer!, StringComparison.Ordinal);
        Assert.Contains("intent_ack", CitizenPeerAck.LastEvent!, StringComparison.Ordinal);
        Assert.Contains("go=plan", CitizenPeerAck.LastEvent!, StringComparison.OrdinalIgnoreCase);

        using var latch = JsonDocument.Parse(File.ReadAllText(CideIntercomVoiceLatch.LatchPath));
        var body = latch.RootElement.GetProperty("body").GetString();
        Assert.Contains("citizen-echo:hands please", body, StringComparison.Ordinal);
        

        using var status = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("done", status.RootElement.GetProperty("status").GetString());
        Assert.Contains("ack=1/1", status.RootElement.GetProperty("peer").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryProcessOnce_persists_operator_dialog_for_multiturn()
    {
        var seatRoot = Path.Combine(_root, "cdp");
        Directory.CreateDirectory(seatRoot);
        CitizenDialogHistory.SetTestPath(Path.Combine(seatRoot, CitizenDialogHistory.FileName));

        WritePending("turn1id00001", "codeword alpha");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        WritePending("turn2id00001", "what codeword?");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        var msgs = CitizenDialogHistory.Load();
        Assert.Equal(4, msgs.Count);
        Assert.Equal("codeword alpha", msgs[0].Content);
        Assert.Contains("citizen-echo:codeword alpha", msgs[1].Content, StringComparison.Ordinal);
        Assert.Equal("what codeword?", msgs[2].Content);
    }

    [Fact]
    public void TryProcessOnce_persists_short_codeword_turn2()
    {
        var seatRoot = Path.Combine(_root, "cdp");
        Directory.CreateDirectory(seatRoot);
        CitizenDialogHistory.SetTestPath(Path.Combine(seatRoot, CitizenDialogHistory.FileName));

        WritePending("turn1id00002", "codeword hold-1201 tango");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        CitizenGlassDialogBridge.TurnOverrideForTests = body =>
            body.Contains("turn2", StringComparison.OrdinalIgnoreCase)
                ? new CitizenCompletions.TurnResult(
                    Ok: true,
                    Error: null,
                    Hint: null,
                    Text: "hold-1201",
                    Model: "test",
                    Provider: "mock",
                    Built: null,
                    WireIntents: null,
                    Routes: null,
                    DryRun: false)
                : EchoTurn(body);
        WritePending("turn2id00002", "turn2-only: reply ONLY one word tango");
        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        var msgs = CitizenDialogHistory.Load();
        Assert.Equal(4, msgs.Count);
        Assert.Equal("turn2-only: reply ONLY one word tango", msgs[2].Content);
        Assert.Equal("hold-1201", msgs[3].Content);
    }

    [Fact]
    public void Start_skips_bridge_loop_on_non_primary_seat()
    {
        IdeIgniteArmHost.BindPrimaryAutoiSeat(false);
        CitizenGlassDialogBridge.Stop();
        CitizenGlassDialogBridge.Start();
        Assert.False(CitizenGlassDialogBridge.IsRunning);
        IdeIgniteArmHost.BindPrimaryAutoiSeat(true);
        CitizenGlassDialogBridge.Start();
        Assert.True(CitizenGlassDialogBridge.IsRunning);
        CitizenGlassDialogBridge.Stop();
    }

    [Fact]
    public void TryProcessOnce_skips_on_non_primary_seat()
    {
        IdeIgniteArmHost.BindPrimaryAutoiSeat(false);
        WritePending("nonprimary01", "skip me");
        Assert.False(CitizenGlassDialogBridge.TryProcessOnce());
        IdeIgniteArmHost.BindPrimaryAutoiSeat(true);
    }

    [Fact]
    public void TryProcessOnce_journals_crew_channel_from_request()
    {
        var id = "crewchan0001";
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body = "crew tagged",
            channel = "crew",
            status = "pending",
            stamped_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        var tail = CideIntercomVoiceLatch.LoadJournalTail(5);
        Assert.NotEmpty(tail);
        Assert.Equal("crew", tail[^1].Channel);
    }

    [Fact]
    public void TryProcessOnce_keeps_long_dialog_prose_not_radio_collapse()
    {
        var longBody = new string('x', 520);
        CitizenGlassDialogBridge.TurnOverrideForTests = _ => EchoTurn(longBody);
        WritePending("longprose001", "long please");

        Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

        using var latch = JsonDocument.Parse(File.ReadAllText(CideIntercomVoiceLatch.LatchPath));
        var body = latch.RootElement.GetProperty("body").GetString();
        Assert.StartsWith("citizen-echo:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Autoi ·", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("→ PFD.NEXT", body!, StringComparison.Ordinal);
    }

    void WritePending(string id, string body, bool resetProcessed = true)
    {
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body,
            status = "pending",
            stamped_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        if (resetProcessed)
            CitizenGlassDialogBridge.ResetProcessedForTests();
    }

    static CitizenCompletions.TurnResult EchoTurn(
        string body,
        IReadOnlyList<CitizenIntentRouter.Route>? routes = null) =>
        new(
            Ok: true,
            Error: null,
            Hint: null,
            Text: "citizen-echo:" + body,
            Model: "test",
            Provider: "mock",
            Built: null,
            WireIntents: null,
            Routes: routes,
            DryRun: false);
}
