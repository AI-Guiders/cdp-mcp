#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Wave32: peer intent_ack after host execute (desk partner duplex).</summary>
public sealed class CitizenPeerAckHostTests
{
    [Fact]
    public void FromExecuted_formats_ack_and_latches_peer()
    {
        CitizenPeerAck.ResetForTests();
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");

        var applied = CitizenRouteHost.Execute(
        [
            CitizenIntentRouter.RouteOne("go=plan"),
            CitizenIntentRouter.RouteOne("go=health")
        ]);
        var ack = CitizenPeerAck.FromExecuted(applied);

        Assert.Equal(2, ack.Applied);
        Assert.Equal(0, ack.Dropped);
        Assert.Equal(1, ack.Generation);
        Assert.Contains("ack=2/2", ack.Peer);
        Assert.Contains("@event peer v0", ack.Event);
        Assert.Contains("intent_ack", ack.Event);
        Assert.Contains("→ applied", ack.Event);
        Assert.Equal(ack.Peer, CitizenPeerAck.LastPeer);
    }

    [Fact]
    public void FromExecuted_surfaces_pulse_on_ack_and_peer_tip()
    {
        CitizenPeerAck.ResetForTests();

        var ack = CitizenPeerAck.FromExecuted(
        [
            new CitizenRouteHost.Applied(
                Raw: "@intent build",
                Verb: "Build",
                Ok: true,
                Action: "build",
                Pulse: "build ok E×0 W×180")
        ]);

        Assert.Equal(1, ack.Applied);
        Assert.Contains("pulse | build ok E×0 W×180", ack.Event, StringComparison.Ordinal);
        Assert.Contains("build ok E×0 W×180", ack.Peer, StringComparison.Ordinal);
        Assert.Equal(ack.Event, CitizenPeerAck.LastEvent);
    }

    [Fact]
    public void Channel_live_mock_turn_surfaces_peer_ack()
    {
        CitizenPeerAck.ResetForTests();
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("p", "plan");
        IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
        IdeDeskSeats.TryPlaceExplicit("m", "browser");

        var payload =
            """{"choices":[{"message":{"role":"assistant","content":"@intent go=plan\n@intent go=health\nok"}}]}""";
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(System.Net.HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"op":"turn","message":"desk needs plan and health — act"}
                """);
            var args = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var json = IdeCitizenChannel.HandleJson(args);
            using var outDoc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(outDoc.RootElement.GetProperty("live_desk").GetBoolean());

            var peer = outDoc.RootElement.GetProperty("peer").GetString();
            Assert.False(string.IsNullOrWhiteSpace(peer));
            Assert.Contains("ack=2/2", peer);

            var peerEvent = outDoc.RootElement.GetProperty("peer_event").GetString();
            Assert.False(string.IsNullOrWhiteSpace(peerEvent));
            Assert.Contains("intent_ack", peerEvent);
            Assert.Contains("→ applied", peerEvent);

            Assert.Equal(peer, CitizenPeerAck.LastPeer);
        }
        finally
        {
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.ResetHttpForTests();
            CitizenPeerAck.ResetForTests();
        }
    }
}