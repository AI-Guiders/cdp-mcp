using Xunit;

namespace CdpMcp.Tests;

public sealed class QualityGatesSnapTests
{
    [Fact]
    public void Snap_ignores_buffers_outside_project_root()
    {
        var rootA = NewTemp("qg-a");
        var rootB = NewTemp("qg-b");
        try
        {
            var fat = Path.Combine(rootA, "Fat.cs");
            var slim = Path.Combine(rootB, "Slim.cs");
            File.WriteAllText(fat, string.Join('\n', Enumerable.Repeat("// line", 500)) + "\nclass Fat {}\n");
            File.WriteAllText(slim, "class Slim {}\n");

            var store = new DocumentBufferStore();
            store.Open(fat);
            store.Open(slim);

            var snap = QualityGates.Snap(store, rootB);
            Assert.True(snap.Enabled);
            Assert.Equal(0, snap.Warn);
            Assert.Equal("ok", snap.Pulse);
        }
        finally
        {
            TryDelete(rootA);
            TryDelete(rootB);
        }
    }

    [Fact]
    public void Snap_warn_counts_unique_files_not_every_method_card()
    {
        var root = NewTemp("qg-methods");
        try
        {
            var path = Path.Combine(root, "Many.cs");
            // Two long methods → previously 2 method_lines warns; Snap should count 1 file.
            var body = """
                namespace T;
                public class Many
                {
                    public void Alpha()
                    {
                """ + string.Join('\n', Enumerable.Repeat("        var x = 1;", 90)) + """
                    }
                    public void Beta()
                    {
                """ + string.Join('\n', Enumerable.Repeat("        var y = 2;", 90)) + """
                    }
                }
                """;
            File.WriteAllText(path, body);
            var store = new DocumentBufferStore();
            store.Open(path);

            var snap = QualityGates.Snap(store, root);
            Assert.Equal(1, snap.Warn);
            Assert.Equal("WARN×1", snap.Pulse);
        }
        finally
        {
            TryDelete(root);
        }
    }

    static string NewTemp(string prefix)
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
