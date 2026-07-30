#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdeDomainChannelTests
{
    [Fact]
    public void Scene_returns_reconstruction_pulse_from_domain_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-domain-ch-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, ".cdp", "domain");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tm.md"), """
            # Domain card: TM
            - id: `tm`
            ## Invariants
            - Feature = Intent
            ## Entry
            - go=plan
            ## Antipatterns
            - Ask without dig
            """);

        var prev = IdeDomainPulse.DirOverrideForTests;
        try
        {
            IdeDomainPulse.DirOverrideForTests = dir;
            var session = new SessionContext
            {
                ProjectRoot = root,
                Phase = CdpPhase.Act,
                Object = CdpObjectKind.Code
            };
            using var doc = JsonDocument.Parse(
                JsonSerializer.Serialize(IdeDomainChannel.Handle(session)));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("domain", doc.RootElement.GetProperty("go").GetString());
            var pulse = doc.RootElement.GetProperty("pulse").GetString() ?? "";
            Assert.Contains("[tm]", pulse, StringComparison.Ordinal);
            Assert.Contains("→", pulse, StringComparison.Ordinal);
            Assert.Contains("≠", pulse, StringComparison.Ordinal);

            using var card = JsonDocument.Parse(
                JsonSerializer.Serialize(IdeDomainChannel.Handle(session, new Dictionary<string, JsonElement>
                {
                    ["op"] = JsonSerializer.SerializeToElement("card"),
                    ["id"] = JsonSerializer.SerializeToElement("tm")
                })));
            Assert.True(card.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("Ask without dig",
                card.RootElement.GetProperty("chain").GetString() ?? "",
                StringComparison.Ordinal);
        }
        finally
        {
            IdeDomainPulse.DirOverrideForTests = prev;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
