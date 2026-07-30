#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdeDomainPulseTests
{
    [Fact]
    public void Parse_and_format_pulse_keeps_A_budget()
    {
        var md = """
            # Domain card: Task Manager

            - id: `tm`
            - organ: plan

            ## Invariants

            - Feature = Intent
            - Leaf AutoI arms next incomplete leaf
            - Do not ask how focus works
            - Extra line four
            - Extra line five should truncate

            ## Entry

            - go=plan
            """;

        Assert.True(IdeDomainPulse.TryParse("tm.md", md, out var card));
        Assert.Equal("tm", card.Id);
        Assert.Contains("Intent", card.Invariants[0], StringComparison.Ordinal);

        var pulse = IdeDomainPulse.FormatPulseA([card], focusHint: "Pressure Domain remount");
        Assert.Contains("[tm]", pulse, StringComparison.Ordinal);
        Assert.Contains("Feature = Intent", pulse, StringComparison.Ordinal);
        Assert.DoesNotContain("Extra line five", pulse, StringComparison.Ordinal);
    }

    [Fact]
    public void Remount_appendix_uses_dir_override_and_focus_scoring()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-domain-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, ".cdp", "domain");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tm.md"), """
            # Domain card: TM
            - id: `tm`
            ## Invariants
            - Feature = Intent
            """);
        File.WriteAllText(Path.Combine(dir, "ignite.md"), """
            # Domain card: Ignite
            - id: `ignite`
            ## Invariants
            - remount-wake protected
            """);

        var prev = IdeDomainPulse.DirOverrideForTests;
        try
        {
            IdeDomainPulse.DirOverrideForTests = dir;
            var appendix = IdeDomainPulse.RemountDomainAppendix(root, "Pressure remount domain pulse");
            Assert.Contains("Domain pulse [A]", appendix, StringComparison.Ordinal);
            Assert.Contains("[ignite]", appendix, StringComparison.Ordinal);

            var charge = IdeIgniteChannel.ComposeRemountInitializedCharge(root, "remount wake leaf");
            Assert.Contains("MCP remounted", charge, StringComparison.Ordinal);
            Assert.Contains("Domain", charge, StringComparison.Ordinal);
            Assert.Contains("[ignite]", charge, StringComparison.Ordinal);
        }
        finally
        {
            IdeDomainPulse.DirOverrideForTests = prev;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Pressure_checklist_includes_Domain_axis()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = Path.Combine(Path.GetTempPath(), "cdp-pressure-domain-" + Guid.NewGuid().ToString("N"));
        var domain = Path.Combine(iso, ".cdp", "domain");
        Directory.CreateDirectory(domain);
        File.WriteAllText(Path.Combine(domain, "tm.md"), "# TM\n- id: `tm`\n## Invariants\n- x\n");
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext
            {
                ProjectRoot = iso,
                Phase = CdpPhase.Act,
                Object = CdpObjectKind.Code
            };
            using var arm = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(
                    IdePressureChannel.Handle(session, new Dictionary<string, System.Text.Json.JsonElement>
                    {
                        ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("arm")
                    })));
            var lines = arm.RootElement.GetProperty("view").GetProperty("lines");
            var joined = string.Join('\n', lines.EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("4 Domain", joined, StringComparison.Ordinal);
            Assert.Contains(".cdp/domain", joined, StringComparison.Ordinal);
        }
        finally
        {
            CdpProfile.ApplyClientRoots([Path.Combine(Path.GetTempPath(), "cdp-pressure-domain-cleanup")]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
