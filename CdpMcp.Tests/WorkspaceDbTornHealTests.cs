#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class WorkspaceDbTornHealTests
{
    [Fact]
    public void IsTornPageException_detects_pageNumber_oor()
    {
        var ex = new ArgumentOutOfRangeException(
            "pageNumber",
            "Page number 9492 is out of range (total: 9492)");
        Assert.True(WorkspaceDbTornHeal.IsTornPageException(ex));
        Assert.True(WorkspaceDbTornHeal.IsTornPageException(new InvalidOperationException("wrap", ex)));
    }

    [Fact]
    public void IsTornPageException_false_for_unrelated()
    {
        Assert.False(WorkspaceDbTornHeal.IsTornPageException(new InvalidOperationException("nope")));
        Assert.False(WorkspaceDbTornHeal.IsTornPageException(
            new ArgumentOutOfRangeException("index", "index out of range")));
    }

    [Fact]
    public void Quarantine_moves_file_and_indexes_sidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-torn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var db = Path.Combine(root, "intent-workspace.witdb");
        var indexes = db + "_indexes";
        File.WriteAllText(db, "torn-bytes");
        Directory.CreateDirectory(indexes);
        File.WriteAllText(Path.Combine(indexes, "x"), "i");
        try
        {
            var bak = WorkspaceDbTornHeal.Quarantine(db);
            Assert.False(File.Exists(db));
            Assert.True(File.Exists(bak));
            Assert.Equal("torn-bytes", File.ReadAllText(bak));
            Assert.False(Directory.Exists(indexes));
            var sidecarBak = Directory.GetDirectories(root)
                .Single(d => d.Contains("_indexes.torn-", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(sidecarBak, "x")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
