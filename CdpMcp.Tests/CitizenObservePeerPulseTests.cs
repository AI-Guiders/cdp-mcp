#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Glass Radio observe: inventory pulse carries real gaps; peer latch survives remount.</summary>
public sealed class CitizenObservePeerPulseTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "cdp-observe-peer-" + Guid.NewGuid().ToString("N"));

    public CitizenObservePeerPulseTests()
    {
        Directory.CreateDirectory(_root);
        CitizenPeerAck.RootOverrideForTests = _root;
        CitizenPeerAck.ResetForTests();
    }

    public void Dispose()
    {
        CitizenPeerAck.ResetForTests();
        CitizenPeerAck.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void TryReadInventoryPulse_includes_pulse_line_and_gap_ids()
    {
        var json = """
            {
              "ok": true,
              "op": "scene",
              "pulse": "inventory · gaps×3 · meta-host CLOSED 4/4 · wave · idle",
              "gaps": [
                { "id": "soft-filelines", "status": "CLOSED", "note": "x" },
                { "id": "throughput-wave", "status": "gap", "note": "y" },
                { "id": "domain-stamp", "status": "habit", "note": "z" }
              ]
            }
            """;

        var pulse = CitizenRouteHost.TryReadInventoryPulse(json, "scene");
        Assert.False(string.IsNullOrWhiteSpace(pulse));
        Assert.Contains("inventory · gaps×3", pulse, StringComparison.Ordinal);
        Assert.Contains("soft-filelines:CLOSED", pulse, StringComparison.Ordinal);
        Assert.Contains("throughput-wave:gap", pulse, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory scene", pulse, StringComparison.Ordinal);
    }

        [Fact]
    public void TryReadInventoryPulse_all_nine_gaps_survive_without_ellipsis()
    {
        var ids = new[]
        {
            "soft-filelines", "citizen-sse", "meta-host-softinstruments", "throughput-wave",
            "pressure-wave-field", "sa-biped", "verify-wave", "domain-stamp", "list-batch-ship"
        };
        var gapJson = string.Join(",\n",
            ids.Select((id, i) =>
                "{ \"id\": \"" + id + "\", \"status\": \"s" + i + "\", \"note\": \"n\" }"));
        var json =
            "{\n" +
            "  \"ok\": true,\n" +
            "  \"op\": \"scene\",\n" +
            "  \"pulse\": \"inventory · gaps×9 · meta-host CLOSED 32/32 · wave · shipping · 3/4 · wave\",\n" +
            "  \"gaps\": [\n" + gapJson + "\n  ]\n" +
            "}";

        var pulse = CitizenRouteHost.TryReadInventoryPulse(json, "scene");
        Assert.False(string.IsNullOrWhiteSpace(pulse));
        Assert.DoesNotContain("…", pulse, StringComparison.Ordinal);
        Assert.Contains("gaps×9", pulse, StringComparison.Ordinal);
        foreach (var id in ids)
            Assert.Contains(id + ":", pulse, StringComparison.Ordinal);
        Assert.True(pulse!.Length <= CitizenRouteHost.InventoryObservePulseMax);
        Assert.True(CitizenPeerAck.EventPulseMax >= CitizenRouteHost.InventoryObservePulseMax);
    }

    [Fact]
    public void Execute_inventory_surfaces_observe_pulse_not_bare_op()
    {
        CitizenRouteHost.InventoryHandleOverride = (_, _) => new
        {
            ok = true,
            op = "scene",
            pulse = "inventory · gaps×2 · meta-host CLOSED 4/4 · wave · shipping 3/4",
            gaps = new object[]
            {
                new { id = "list-batch-ship", status = "canon", note = "x" },
                new { id = "sa-biped", status = "afford", note = "y" }
            }
        };
        IdeDeskSeats.EnsureDefaultsFromSettings();

        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("inventory scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Contains("gaps×2", applied[0].Pulse!, StringComparison.Ordinal);
            Assert.Contains("list-batch-ship:canon", applied[0].Pulse!, StringComparison.Ordinal);

            var ack = CitizenPeerAck.FromExecuted(applied);
            Assert.Contains("pulse |", ack.Event, StringComparison.Ordinal);
            Assert.Contains("gaps×2", ack.Event, StringComparison.Ordinal);
            Assert.Contains("list-batch-ship", ack.Event, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.InventoryHandleOverride = null;
        }
    }

    [Fact]
    public void PeerAck_latch_survives_memory_drop_like_remount()
    {
        var ack = CitizenPeerAck.FromExecuted(
        [
            new CitizenRouteHost.Applied(
                Raw: "@intent inventory scene",
                Verb: "Inventory",
                Ok: true,
                Action: "inventory",
                Go: "inventory",
                Pulse: "inventory · gaps×2 · list-batch-ship:canon sa-biped:afford")
        ]);

        Assert.True(File.Exists(CitizenPeerAck.LatchPath));
        Assert.Contains("gaps×2", ack.Event, StringComparison.Ordinal);

        CitizenPeerAck.DropMemoryForTests();
        // Remount: next read hydrates durable latch.
        var reloaded = CitizenPeerAck.LastEvent;
        Assert.False(string.IsNullOrWhiteSpace(reloaded));
        Assert.Contains("gaps×2", reloaded!, StringComparison.Ordinal);
        Assert.Contains("ack=1/1", CitizenPeerAck.LastPeer!, StringComparison.Ordinal);
    }
}
