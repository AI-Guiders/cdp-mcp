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
    public void AfterHands_noops_without_peerAck()
    {
        Assert.False(CitizenResultWake.AfterHands(null));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void AfterHands_noops_on_wake_charge_body()
    {
        Assert.False(CitizenResultWake.AfterHands(SampleAck, requestBody: CitizenResultWake.PeerReadyCharge));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void AfterHands_noops_when_same_turn_observe_ran()
    {
        Assert.False(CitizenResultWake.AfterHands(SampleAck, sameTurnObserveRan: true));
        Assert.False(File.Exists(CitizenGlassDialogBridge.RequestPath));
    }

    [Fact]
    public void AfterHands_arms_peer_ready_when_latch_clear()
    {
        Assert.True(CitizenResultWake.AfterHands(SampleAck, channel: "radio", requestBody: "hands"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(CitizenResultWake.PeerReadyCharge, doc.RootElement.GetProperty("body").GetString());
        Assert.Equal("radio", doc.RootElement.GetProperty("channel").GetString());
    }

    [Fact]
    public void AfterHands_arms_when_latch_status_done()
    {
        WriteLatch("done00000001", "hands please", "done");

        Assert.True(CitizenResultWake.AfterHands(SampleAck, requestBody: "hands please"));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenGlassDialogBridge.RequestPath));
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
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
