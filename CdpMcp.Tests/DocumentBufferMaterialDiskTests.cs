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

    [Fact]
    public void Flush_refuses_material_disk_drift_without_force()
    {
        var dir = NewTempDir("flush-drift-refuse");
        try
        {
            var path = Path.Combine(dir, "Drift.cs");
            File.WriteAllText(path, "class Drift {}");

            var store = new DocumentBufferStore();
            var buf = store.Open(path);
            buf.Text = "class Drift { int y; }";
            buf.Dirty = true;

            File.WriteAllText(path, "class Drift { int x; }");
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

            var ex = Assert.Throws<InvalidOperationException>(() => store.Flush(buf, allowShrink: true));
            Assert.Contains("material disk drift", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("force", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(buf.Dirty);
            Assert.Equal("class Drift { int x; }", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Flush_force_overwrites_material_disk_drift()
    {
        var dir = NewTempDir("flush-drift-force");
        try
        {
            var path = Path.Combine(dir, "Force.cs");
            File.WriteAllText(path, "class Force {}");

            var store = new DocumentBufferStore();
            var buf = store.Open(path);
            buf.Text = "class Force { int y; }";
            buf.Dirty = true;

            File.WriteAllText(path, "class Force { int x; }");
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

            store.Flush(buf, allowShrink: true, force: true);

            Assert.False(buf.Dirty);
            Assert.Equal("class Force { int y; }", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Flush_dirty_without_external_mtime_does_not_need_force()
    {
        var dir = NewTempDir("flush-dirty-ok");
        try
        {
            var path = Path.Combine(dir, "Ok.cs");
            File.WriteAllText(path, "class Ok {}");

            var store = new DocumentBufferStore();
            var buf = store.Open(path);
            buf.Text = "class Ok { int z; }";
            buf.Dirty = true;

            store.Flush(buf, allowShrink: true);

            Assert.False(buf.Dirty);
            Assert.Equal("class Ok { int z; }", File.ReadAllText(path));
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
