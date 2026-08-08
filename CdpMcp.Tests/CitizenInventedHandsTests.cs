#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenInventedHandsTests
{
    [Fact]
    public void LooksLikeHandsClaim_detects_sdelala_line()
    {
        Assert.True(CitizenInventedHands.LooksLikeHandsClaim(
            "Ищу исходники.\n\nСделала: find cascade-ide where=project"));
        Assert.False(CitizenInventedHands.LooksLikeHandsClaim("просто текст без рук"));
    }

    [Fact]
    public void TryRecoverRoutes_parses_find_claims_not_desk_labels()
    {
        var routes = CitizenInventedHands.TryRecoverRoutes(
            "Света, копаю.\n\nСделала: find cascade-ide where=project · find Glass where=project · find_desk");

        Assert.Equal(2, routes.Count);
        Assert.All(routes, r => Assert.True(r.Ok));
        Assert.Contains(routes, r => r.Raw.Contains("cascade-ide", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(routes, r => r.Raw.Contains("Glass", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryRecoverRoutes_normalizes_invented_desk_layout_to_presentation_set()
    {
        var routes = CitizenInventedHands.TryRecoverRoutes(
            "Переключаю топологию.\n\nСделала: desk layout=\"(P/M)(F)\"");

        Assert.Single(routes);
        Assert.True(routes[0].Ok);
        Assert.Contains("presentation_set", routes[0].Raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(P/M)(F)", routes[0].Raw, StringComparison.Ordinal);
    }


    [Fact]
    public void TryProcessOnce_recovers_invented_find_hands_and_arms_peer_ready()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-invented-hands-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            CitizenGlassDialogBridge.RootOverrideForTests = root;
            CitizenPeerAck.RootOverrideForTests = root;
            CideIntercomVoiceLatch.RootOverrideForTests = root;
            CitizenGlassDialogBridge.ResetProcessedForTests();
            CitizenPeerAck.ResetForTests();
            IdeCitizenChannel.InviteReadyOverrideForTests = () => true;
            IdeIgniteArmHost.BindPrimaryAutoiSeat(true);

            IdeDeskSeats.EnsureDefaultsFromSettings();
            IdeDeskSeats.Clear();
            IdeDeskSeats.TryPlaceExplicit("p", "plan");

            var seatRoot = Path.Combine(root, "cdp");
            Directory.CreateDirectory(seatRoot);
            CitizenDialogHistory.SetTestPath(Path.Combine(seatRoot, CitizenDialogHistory.FileName));

            CitizenRouteHost.FindCallOverride = _ =>
                new { ok = true, pulse = "find · 2 hits · cascade-ide" };

            CitizenGlassDialogBridge.TurnOverrideForTests = body =>
            {
                if (CitizenResultWake.IsWakeCharge(body))
                    return Echo("wake: saw find pulse");
                if (body.Equals(CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal))
                    return Echo("observe: find pulse seen — cascade-ide under open/");
                // Invented hands — no @intent routes (lived SoftFL).
                return Echo("Копаю CIDE→Glass.\n\nСделала: find cascade-ide where=project");
            };

            WritePending("invhands0001", "проанализируй CascadeIDE");
            Assert.True(CitizenGlassDialogBridge.TryProcessOnce());

            using (var afterHands = System.Text.Json.JsonDocument.Parse(
                       File.ReadAllText(CitizenGlassDialogBridge.RequestPath)))
            {
                // Observe ran — still arms peer_ready for Completions #3 (contour self-flight).
                Assert.Equal("pending", afterHands.RootElement.GetProperty("status").GetString());
                Assert.Equal(
                    CitizenResultWake.PeerReadyCharge,
                    afterHands.RootElement.GetProperty("body").GetString());
            }

            Assert.NotNull(CitizenPeerAck.LastPeer);
            Assert.Contains("ack=", CitizenPeerAck.LastPeer, StringComparison.OrdinalIgnoreCase);

            Assert.True(CitizenGlassDialogBridge.TryProcessOnce());
            Assert.False(CitizenGlassDialogBridge.TryProcessOnce());
        }
        finally
        {
            CitizenGlassDialogBridge.TurnOverrideForTests = null;
            CitizenRouteHost.FindCallOverride = null;
            CitizenGlassDialogBridge.RootOverrideForTests = null;
            CitizenPeerAck.RootOverrideForTests = null;
            CideIntercomVoiceLatch.RootOverrideForTests = null;
            IdeCitizenChannel.InviteReadyOverrideForTests = null;
            IdeIgniteArmHost.BindPrimaryAutoiSeat(null);
            CitizenDialogHistory.SetTestPath(null);
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    static CitizenCompletions.TurnResult Echo(string text) =>
        new(
            Ok: true,
            Error: null,
            Hint: null,
            Text: text,
            Model: "test",
            Provider: "mock",
            Built: null,
            WireIntents: null,
            Routes: null,
            DryRun: false);

    static void WritePending(string id, string body)
    {
        var req = new
        {
            schema = CitizenGlassDialogBridge.Schema,
            id,
            body,
            channel = "radio",
            status = "pending",
            stamped_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            CitizenGlassDialogBridge.RequestPath,
            System.Text.Json.JsonSerializer.Serialize(
                req,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
                }));
    }
}
