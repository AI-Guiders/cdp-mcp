#nullable enable
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdeStandingPulseTests
{
    [Fact]
    public void Remount_standing_appendix_and_remount_charge()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-rules-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, ".cdp", "rules");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "healthy-agent.md"), """
            # Standing rule: Healthy agent
            - id: `healthy-agent`
            ## Invariants
            - dig/parallel not biped serial
            ## Entry
            - go=rules
            ## Antipatterns
            - Cursor dump as CDP SSOT
            """);

        var prev = IdeStandingPulse.DirOverrideForTests;
        try
        {
            IdeStandingPulse.DirOverrideForTests = dir;
            var appendix = IdeStandingPulse.RemountStandingAppendix(root, "healthy-agent remount");
            Assert.Contains("Standing rules [A]", appendix, StringComparison.Ordinal);
            Assert.Contains("[healthy-agent]", appendix, StringComparison.Ordinal);

            var charge = IdeIgniteChannel.ComposeRemountInitializedCharge(root, "healthy-agent remount");
            Assert.Contains("MCP remounted", charge, StringComparison.Ordinal);
            Assert.Contains("Standing rules", charge, StringComparison.Ordinal);
            Assert.Contains("[healthy-agent]", charge, StringComparison.Ordinal);
            Assert.Contains("Body recall", charge, StringComparison.Ordinal);

            var session = new SessionContext { ProjectRoot = root };
            var scene = IdeRulesChannel.Handle(session, null);
            var json = System.Text.Json.JsonSerializer.Serialize(scene);
            Assert.Contains("rules_channel/v0", json, StringComparison.Ordinal);
            Assert.Contains("healthy-agent", json, StringComparison.Ordinal);
            Assert.Contains("rules ·", IdeRulesChannel.PulseLine(session), StringComparison.Ordinal);
        }
        finally
        {
            IdeStandingPulse.DirOverrideForTests = prev;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
