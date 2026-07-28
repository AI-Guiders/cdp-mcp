using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class DocumentDiskSyncLatchTests : IDisposable
{
    readonly string _root;

    public DocumentDiskSyncLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-disk-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DocumentDiskSyncLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        DocumentDiskSyncLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_writes_agent_origin_latch()
    {
        var path = Path.Combine(_root, "Foo.cs");
        File.WriteAllText(path, "// x");
        DocumentDiskSyncLatch.Publish(path, DocumentDiskSyncLatch.OriginAgent);

        Assert.True(File.Exists(DocumentDiskSyncLatch.LatchPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(DocumentDiskSyncLatch.LatchPath));
        Assert.Equal(DocumentDiskSyncLatch.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("agent", doc.RootElement.GetProperty("origin").GetString());
        Assert.Equal(Path.GetFullPath(path), doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Flush_publishes_agent_disk_sync()
    {
        var path = Path.Combine(_root, "Bar.cs");
        File.WriteAllText(path, "class A {}");
        var store = new DocumentBufferStore();
        var buf = store.Open(path);
        buf.Text = "class B {}";
        buf.Dirty = true;
        store.Flush(buf, allowShrink: true);

        Assert.False(buf.Dirty);
        var latch = DocumentDiskSyncLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(DocumentDiskSyncLatch.OriginAgent, latch!.Origin);
        Assert.Equal(Path.GetFullPath(path), latch.Path);
    }
}
