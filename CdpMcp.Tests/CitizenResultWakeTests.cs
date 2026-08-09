#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection(nameof(IntercomLatchSerial))]
public sealed class CitizenResultWakeTests : IDisposable
{
    readonly string _root;
    static readonly CitizenPeerAck.Result SampleAck =
        new("ack=1/1", "intent_ack", 1, 0, 1);

    public CitizenResultWakeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-result-wake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CitizenGlassDialogBridge.RootOverrideForTests = _root;
        IdeCitizenChannel.InviteReadyOverrideForTests = () => true;
        CitizenGlassDialogBridge.ResetProcessedForTests();
    }

    public void Dispose()
    {
        CitizenGlassDialogBridge.RootOverrideForTests = null;
        IdeCitizenChannel.InviteReadyOverrideForTests = null;
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
    public void PeerReadyCharge_steers_next_hand_not_radio_done()
    {
        Assert.Contains("Radio alone", CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.DoesNotContain("One short Radio letter", CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.Contains("find≠next hand", CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.DoesNotContain("take|replace|find", CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.Contains(CitizenResultWake.LeafTakePath, CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.Contains("PASTE", CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.Contains("dialog-history basenames", CitizenResultWake.PeerReadyCharge, StringComparison.Ordinal);
        Assert.Contains("partner approve", CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal);
        Assert.DoesNotContain("жду вектора", CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal);
        Assert.DoesNotContain("One short Radio letter", CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal);
        Assert.Contains("find≠fabricate next", CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal);
        Assert.DoesNotContain("Next hand now", CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal);
        Assert.Contains("меняй", CitizenResultWake.PeerReadyNextOpenCharge, StringComparison.Ordinal);
        Assert.DoesNotContain("жду вектора", CitizenResultWake.PeerReadyNextOpenCharge, StringComparison.Ordinal);
        Assert.DoesNotContain(CitizenResultWake.LeafTakePath, CitizenResultWake.PeerReadyNextOpenCharge, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterHands_noops_without_peerAck()
    {
        Assert.False(CitizenResultWake.AfterHands(null));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void AfterHands_noops_on_wake_charge_body_when_all_applied()
    {
        Assert.False(CitizenResultWake.AfterHands(SampleAck, requestBody: CitizenResultWake.PeerReadyCharge));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void AfterHands_arms_when_same_turn_observe_ran()
    {
        // SoftFL densify 2026-08-09b: observe ≠ stop — arm PASTE leaf (not next_open anti-agency).
        Assert.True(CitizenResultWake.AfterHands(SampleAck, sameTurnObserveRan: true, requestBody: "hands"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(CitizenResultWake.PeerReadyCharge, doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void AfterHands_arms_dig_when_peer_ready_had_invent_or_fnf_drop()
    {
        // A2: invent/FileNotFound without dig credit → dig charge, not take-retry.
        var dropped = new CitizenPeerAck.Result("ack=0/1 FileNotFound: GlassIntercom.cs", "intent_dropped", 0, 1, 1);
        Assert.True(CitizenResultWake.AfterHands(dropped, requestBody: CitizenResultWake.PeerReadyCharge));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
        var body = doc.RootElement.GetProperty("body").GetString()!;
        Assert.StartsWith("reason=peer_ready_dig", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drop=[", body, StringComparison.Ordinal);
        Assert.Contains("GlassIntercomMention", body, StringComparison.Ordinal);
        Assert.Contains("FileNotFound", body, StringComparison.Ordinal);
        Assert.Contains("context only", body, StringComparison.Ordinal);
        Assert.DoesNotContain("densify THAT path", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterHands_arms_retry_when_dig_credit_and_invent_drop()
    {
        var dropped = new CitizenPeerAck.Result("ack=0/1 FileNotFound: GlassIntercom.cs", "intent_dropped", 0, 1, 1);
        Assert.True(CitizenResultWake.AfterHands(
            dropped,
            requestBody: CitizenResultWake.PeerReadyCharge,
            sameTurnObserveRan: true));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        var body = doc.RootElement.GetProperty("body").GetString()!;
        Assert.StartsWith("reason=peer_ready_retry", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AfterHands_arms_take_peer_ready_after_dig_applied()
    {
        Assert.True(CitizenResultWake.AfterHands(SampleAck, requestBody: CitizenResultWake.PeerReadyDigCharge));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal(CitizenResultWake.PeerReadyCharge, doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void AfterHands_arms_invent_halt_when_retry_had_invent_drops()
    {
        // A3: invent on take-retry → halt (not endless retry2 paste thrash).
        var dropped = new CitizenPeerAck.Result("ack=0/1 FileNotFound: CascadeIDE.cs", "intent_dropped", 0, 1, 1);
        var retryBody = CitizenResultWake.FormatDropCharge(CitizenResultWake.PeerReadyRetryCharge, dropped);
        Assert.True(CitizenResultWake.AfterHands(dropped, requestBody: retryBody));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        var body = doc.RootElement.GetProperty("body").GetString()!;
        Assert.StartsWith("reason=peer_ready_invent_halt", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AfterHands_arms_retry2_when_retry_had_drops()
    {
        var dropped = new CitizenPeerAck.Result("ack=0/1 still missing", "intent_dropped", 0, 1, 1);
        var retryBody = CitizenResultWake.FormatDropCharge(CitizenResultWake.PeerReadyRetryCharge, dropped);
        Assert.True(CitizenResultWake.AfterHands(dropped, requestBody: retryBody));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        var body = doc.RootElement.GetProperty("body").GetString()!;
        Assert.StartsWith("reason=peer_ready_retry2", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drop=[", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterHands_kb_drop_arms_kb_charge_not_take_leaf()
    {
        var dropped = new CitizenPeerAck.Result(
            "ack=0/1 kb memory_world read_knowledge_file missing",
            "intent_dropped",
            0,
            1,
            1);
        Assert.True(CitizenResultWake.IsKbDrop(dropped));
        Assert.True(CitizenResultWake.AfterHands(dropped, requestBody: "dialog dig"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        var body = doc.RootElement.GetProperty("body").GetString()!;
        Assert.StartsWith("reason=peer_ready_kb", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PASTE leaf take", body, StringComparison.Ordinal);
        Assert.Contains("file_path=", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterHands_kb_applied_arms_kb_not_mention_take()
    {
        var applied = new CitizenPeerAck.Result(
            "ack=1/1 kb memory_world read_knowledge_file ok",
            "intent_ack",
            1,
            0,
            1);
        Assert.True(CitizenResultWake.AfterHands(applied, requestBody: "сходи в knowledge"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal(CitizenResultWake.PeerReadyKbCharge, doc.RootElement.GetProperty("body").GetString());
        Assert.DoesNotContain(CitizenResultWake.LeafTakePath, doc.RootElement.GetProperty("body").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterHands_noops_on_retry2_charge_even_with_drops()
    {
        var dropped = new CitizenPeerAck.Result("ack=0/1", "intent_dropped", 0, 1, 1);
        var retry2 = CitizenResultWake.FormatDropCharge(CitizenResultWake.PeerReadyRetry2Charge, dropped);
        Assert.False(CitizenResultWake.AfterHands(dropped, requestBody: retry2));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void AfterHands_arms_paste_leaf_when_latch_clear()
    {
        Assert.True(CitizenResultWake.AfterHands(SampleAck, channel: "radio", requestBody: "hands"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(CitizenResultWake.PeerReadyCharge, doc.RootElement.GetProperty("body").GetString());
        Assert.Equal("radio", doc.RootElement.GetProperty("channel").GetString());
    }

    [Fact]
    public void AfterHands_arms_paste_leaf_when_latch_status_done()
    {
        WriteLatch("done00000001", "hands please", "done");

        Assert.True(CitizenResultWake.AfterHands(SampleAck, requestBody: "hands please"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(CitizenResultWake.PeerReadyCharge, doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void AfterHands_noops_on_next_open_charge_when_all_applied()
    {
        Assert.False(CitizenResultWake.AfterHands(SampleAck, requestBody: CitizenResultWake.PeerReadyNextOpenCharge));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void SelectAppliedWakeCharge_defaults_to_paste_leaf_not_next_open()
    {
        Assert.Equal(CitizenResultWake.PeerReadyCharge, CitizenResultWake.SelectAppliedWakeCharge(SampleAck, "hands"));
        Assert.Equal(
            CitizenResultWake.PeerReadyNextOpenCharge,
            CitizenResultWake.SelectAppliedWakeCharge(SampleAck, CitizenResultWake.PeerReadyNextOpenCharge));
    }

    [Fact]
    public void AfterHands_leaf_contour_keeps_paste_take()
    {
        Assert.True(CitizenResultWake.AfterHands(
            SampleAck,
            requestBody: "continue SoftFL densify " + CitizenResultWake.LeafTakePath));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal(CitizenResultWake.PeerReadyCharge, doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void AfterHands_skips_when_pending_human_latch()
    {
        WriteLatch("human0000001", "operator Send", "pending");

        Assert.False(CitizenResultWake.AfterHands(SampleAck, requestBody: "hands"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("operator Send", doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void AfterHands_idempotent_when_peer_ready_already_pending()
    {
        Assert.True(CitizenResultWake.AfterHands(SampleAck, requestBody: "hands"));
        var first = File.ReadAllText(CitizenGlassDialogBridge.RequestPath);

        Assert.False(CitizenResultWake.AfterHands(SampleAck, requestBody: "hands again"));
        Assert.Equal(first, File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
    }

    static void WriteLatch(string id, string body, string status)
    {
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body,
            status,
            stamped_utc = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(CitizenGlassDialogBridge.StateRoot);
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
    }
}
