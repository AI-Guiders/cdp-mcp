using Xunit;

namespace CdpMcp.Tests;

public sealed class DocumentBufferMaterialDiskTests
{
    [Fact]
    public void Scene_mtime_only_same_content_does_not_count_as_disk_changed()
    {
        var dir = NewTempDir("mtime-same");
        try
        {
            var path = Path.Combine(dir, "Same.cs");
            File.WriteAllText(path, "class Same {}");

            var store = new DocumentBufferStore();
            store.Open(path);

            // Touch mtime without changing bytes (git checkout / copy stamp).
            var stamp = File.GetLastWriteTimeUtc(path).AddSeconds(5);
            File.SetLastWriteTimeUtc(path, stamp);

            var scene = store.Scene();
            var json = System.Text.Json.JsonSerializer.Serialize(scene);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(0, doc.RootElement.GetProperty("disk_changed_count").GetInt32());
            Assert.False(doc.RootElement.GetProperty("docs")[0].GetProperty("disk_changed").GetBoolean());
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Scene_content_diff_still_counts_as_disk_changed()
    {
        var dir = NewTempDir("mtime-diff");
        try
        {
            var path = Path.Combine(dir, "Diff.cs");
            File.WriteAllText(path, "class Diff {}");

            var store = new DocumentBufferStore();
            store.Open(path);
            File.WriteAllText(path, "class Diff { int x; }");
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

            var scene = store.Scene();
            var json = System.Text.Json.JsonSerializer.Serialize(scene);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("disk_changed_count").GetInt32());
            Assert.True(doc.RootElement.GetProperty("docs")[0].GetProperty("disk_changed").GetBoolean());
        }
        finally
        {
            TryDelete(dir);
        }
    }

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
