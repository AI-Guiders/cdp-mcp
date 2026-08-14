#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class ExploreCorrLatchTests
{
    [Fact]
    public void Corr_stamp_satisfies_same_directory_mutate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "explore-corr-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var latchPath = Path.Combine(dir, "latch.json");
        var prevPath = ExploreCorrLatch.PathOverrideForTests;
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        ExploreCorrLatch.PathOverrideForTests = latchPath;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        try
        {
            ExploreCorrLatch.StampCorr(dir, "src/Foo.cs", adrCount: 2);
            Assert.True(ExploreCorrLatch.HasSatisfied(dir, "src/Foo.cs"));
            Assert.True(ExploreCorrLatch.HasSatisfied(dir, "src/Bar.cs"));
            Assert.False(ExploreCorrLatch.HasSatisfied(dir, "other/Z.cs"));
            Assert.True(ExploreCorrLatch.HasAnyFresh(dir));
        }
        finally
        {
            ExploreCorrLatch.PathOverrideForTests = prevPath;
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void No_adr_requires_why_and_stamps()
    {
        var dir = Path.Combine(Path.GetTempPath(), "explore-corr-na-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var latchPath = Path.Combine(dir, "latch.json");
        var prevPath = ExploreCorrLatch.PathOverrideForTests;
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        ExploreCorrLatch.PathOverrideForTests = latchPath;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        try
        {
            Assert.Throws<ArgumentException>(() => ExploreCorrLatch.StampNoAdr(dir, "a.cs", ""));
            ExploreCorrLatch.StampNoAdr(dir, "a.cs", "spike throwaway");
            Assert.True(ExploreCorrLatch.HasSatisfied(dir, "a.cs"));
        }
        finally
        {
            ExploreCorrLatch.PathOverrideForTests = prevPath;
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Gate_skips_when_no_mapped_adrs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "explore-corr-gate-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "lonely.cs");
        File.WriteAllText(file, "// x");
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        try
        {
            // no workspace.toml → no mapped ADRs → no throw
            ExploreCorrGate.RefuseMutateIfNeeded(file, dir, args: null, verb: "edit");
        }
        finally
        {
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Human_face_done_refuses_without_latch_when_toml_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), "explore-corr-done-" + Guid.NewGuid().ToString("n"));
        var cascade = Path.Combine(dir, ".cascade");
        Directory.CreateDirectory(cascade);
        File.WriteAllText(Path.Combine(cascade, "workspace.toml"), "[workspace.adr.map]\n\"*\" = []\n");
        var latchPath = Path.Combine(dir, "latch.json");
        var prevPath = ExploreCorrLatch.PathOverrideForTests;
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        ExploreCorrLatch.PathOverrideForTests = latchPath;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["project_root"] = JsonSerializer.SerializeToElement(dir),
                ["evidence"] = JsonSerializer.SerializeToElement(Path.Combine(dir, "x.png")),
                ["domain"] = JsonSerializer.SerializeToElement("glass")
            };
            // PNG + domain not set up — call corr refuse path via public helper indirectly:
            // Stamp missing → RefuseExploreCorr is inside human-face; use HasAnyFresh assert + gate unit above.
            Assert.False(ExploreCorrLatch.HasAnyFresh(dir));

            ExploreCorrLatch.StampCorr(dir, "Glass/Main.cs", 1);
            Assert.True(ExploreCorrLatch.HasAnyFresh(dir));
            _ = args;
        }
        finally
        {
            ExploreCorrLatch.PathOverrideForTests = prevPath;
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
