#nullable enable
using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

public sealed class ExploreCorrPolicyTests
{
    [Fact]
    public void Off_rule_skips_gate_even_when_adr_mapped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "explore-tier-" + Guid.NewGuid().ToString("n"));
        var cascade = Path.Combine(dir, ".cascade");
        Directory.CreateDirectory(cascade);
        File.WriteAllText(
            Path.Combine(cascade, "workspace.toml"),
            """
            [workspace.adr.map]
            "*" = ["README.md"]

            [workspace.explore_corr]
            default = "full"

            [[workspace.explore_corr.rules]]
            path = "knowledge/work/projects/"
            mode = "off"
            """);
        var cardDir = Path.Combine(dir, "knowledge", "work", "projects", "foo");
        Directory.CreateDirectory(cardDir);
        var card = Path.Combine(cardDir, "README.md");
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        try
        {
            Assert.Equal(ExploreCorrPolicy.Mode.Off, ExploreCorrPolicy.ResolveMode(card, dir));
            ExploreCorrGate.RefuseMutateIfNeeded(card, dir, args: null, verb: "edit");
        }
        finally
        {
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Card_mode_allows_create_on_missing_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "explore-card-" + Guid.NewGuid().ToString("n"));
        var cascade = Path.Combine(dir, ".cascade");
        Directory.CreateDirectory(cascade);
        File.WriteAllText(
            Path.Combine(cascade, "workspace.toml"),
            """
            [workspace.adr.map]
            "*" = ["README.md"]

            [[workspace.explore_corr.rules]]
            path = "knowledge/work/"
            mode = "card"
            """);
        var newFile = Path.Combine(dir, "knowledge", "work", "new-card.md");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        try
        {
            Assert.Equal(ExploreCorrPolicy.Mode.Card, ExploreCorrPolicy.ResolveMode(newFile, dir));
            ExploreCorrGate.RefuseMutateIfNeeded(
                newFile, dir, args: null, verb: "create", pathExistedBefore: false);
        }
        finally
        {
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
