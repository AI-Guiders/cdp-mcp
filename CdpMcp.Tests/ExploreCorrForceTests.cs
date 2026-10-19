#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// CDP-ADR-0031: дверь остаётся (force=true), но проход виден и требует причины с источником.
/// Молчаливый обход неотличим от поломки контроля — поэтому фиксируется штампом.
/// </summary>
public sealed class ExploreCorrForceTests
{
    static IReadOnlyDictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    static (string Root, string File, string Readme) Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "explore-force-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, ".cascade"));
        File.WriteAllText(
            Path.Combine(root, ".cascade", "workspace.toml"),
            """
            [workspace.adr.map]
            "*" = ["README.md"]
            """);
        var readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "docs");
        var file = Path.Combine(root, "src", "app", "Foo.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "// code");
        return (root, file, readme);
    }

    [Fact]
    public void Force_without_why_is_refused()
    {
        var (root, file, _) = Fixture();
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        var prevPath = ExploreCorrLatch.PathOverrideForTests;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        ExploreCorrLatch.PathOverrideForTests = Path.Combine(root, "latch.json");
        try
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                ExploreCorrGate.RefuseMutateIfNeeded(
                    file, root, Args("""{"force":true}"""), verb: "edit"));

            Assert.Contains("force_why_required", ex.Message);
        }
        finally
        {
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            ExploreCorrLatch.PathOverrideForTests = prevPath;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Force_with_vacuous_why_is_refused()
    {
        var (root, file, _) = Fixture();
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        var prevPath = ExploreCorrLatch.PathOverrideForTests;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        ExploreCorrLatch.PathOverrideForTests = Path.Combine(root, "latch.json");
        try
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                ExploreCorrGate.RefuseMutateIfNeeded(
                    file, root, Args("""{"force":true,"why":"к вопросу не относится"}"""), verb: "edit"));

            Assert.Contains("force_why_required", ex.Message);
        }
        finally
        {
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            ExploreCorrLatch.PathOverrideForTests = prevPath;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Force_with_source_passes_and_leaves_a_stamp()
    {
        var (root, file, _) = Fixture();
        var prevEn = ExploreCorrLatch.EnabledOverrideForTests;
        var prevPath = ExploreCorrLatch.PathOverrideForTests;
        ExploreCorrLatch.EnabledOverrideForTests = true;
        ExploreCorrLatch.PathOverrideForTests = Path.Combine(root, "latch.json");
        try
        {
            ExploreCorrGate.RefuseMutateIfNeeded(
                file, root, Args("""{"force":true,"why":"смотрел README.md — к локусу не относится"}"""), verb: "edit");

            Assert.True(ExploreCorrLatch.HasSatisfied(root, "src/app/Foo.cs"));
            Assert.True(ExploreCorrLatch.TryRead(root, out var stamp));
            Assert.Equal(ExploreCorrLatch.KindForce, stamp!.Kind);
            Assert.Contains("README.md", stamp.Why);
        }
        finally
        {
            ExploreCorrLatch.EnabledOverrideForTests = prevEn;
            ExploreCorrLatch.PathOverrideForTests = prevPath;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
