using Xunit;

namespace CdpMcp.Tests;

public sealed class DocumentBufferStoreParkTests
{
    [Fact]
    public void ParkOutsideProject_closes_clean_foreign_keeps_in_project()
    {
        var rootA = NewTempDir("park-a");
        var rootB = NewTempDir("park-b");
        try
        {
            var fileA = Path.Combine(rootA, "A.cs");
            var fileB = Path.Combine(rootB, "B.cs");
            File.WriteAllText(fileA, "class A {}");
            File.WriteAllText(fileB, "class B {}");

            var store = new DocumentBufferStore();
            store.Open(fileA);
            store.Open(fileB);
            Assert.Equal(2, store.All.Count);

            var park = store.ParkOutsideProject(rootB);
            Assert.Equal(1, park.ClosedClean);
            Assert.Empty(park.KeptDirty);
            Assert.NotNull(park.Note);
            Assert.Single(store.All);
            Assert.Contains(store.All, b => b.Path.Equals(fileB, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(rootA);
            TryDelete(rootB);
        }
    }

    [Fact]
    public void ParkOutsideProject_keeps_dirty_foreign()
    {
        var rootA = NewTempDir("park-dirty-a");
        var rootB = NewTempDir("park-dirty-b");
        try
        {
            var fileA = Path.Combine(rootA, "Dirty.cs");
            var fileB = Path.Combine(rootB, "Keep.cs");
            File.WriteAllText(fileA, "class Dirty {}");
            File.WriteAllText(fileB, "class Keep {}");

            var store = new DocumentBufferStore();
            var dirty = store.Open(fileA);
            store.ApplySetText(dirty, "class Dirty { int x; }");
            store.Open(fileB);

            var park = store.ParkOutsideProject(rootB);
            Assert.Equal(0, park.ClosedClean);
            Assert.Single(park.KeptDirty);
            Assert.Equal(2, store.All.Count);
            Assert.Contains("dirty", park.Note, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(rootA);
            TryDelete(rootB);
        }
    }

    [Theory]
    [InlineData(@"C:\proj\src\A.cs", @"C:\proj", true)]
    [InlineData(@"C:\proj", @"C:\proj", true)]
    [InlineData(@"C:\other\A.cs", @"C:\proj", false)]
    public void IsUnderProjectRoot_prefix_contract(string path, string root, bool expected)
        => Assert.Equal(expected, DocumentBufferStore.IsUnderProjectRoot(path, root));

    static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-mcp-tests", prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }
}
