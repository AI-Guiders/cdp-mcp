#nullable enable
using CdpMcp;
using Xunit;

namespace CdpMcp.Tests;

public class SharedFileIndicationTests : IDisposable
{
    readonly string _root;

    public SharedFileIndicationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-shared-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SharedFileIndication.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        SharedFileIndication.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void IsShared_true_when_human_path_in_agent_buffers()
    {
        var human = Path.Combine(_root, "A.cs");
        var other = Path.Combine(_root, "B.cs");
        Assert.True(SharedFileIndication.IsShared(human, [other, human]));
    }

    [Fact]
    public void IsShared_false_when_no_overlap()
    {
        var human = Path.Combine(_root, "A.cs");
        var other = Path.Combine(_root, "B.cs");
        Assert.False(SharedFileIndication.IsShared(human, [other]));
    }

    [Fact]
    public void PathsReferToSameFile_is_case_insensitive_full_path()
    {
        var a = Path.Combine(_root, "x.cs");
        Assert.True(SharedFileIndication.PathsReferToSameFile(a, a.ToUpperInvariant()));
    }

    [Fact]
    public void Publish_writes_shared_latch()
    {
        var path = Path.Combine(_root, "Shared.cs");
        File.WriteAllText(path, "//");
        SharedFileIndication.Publish(path, shared: true);
        Assert.True(File.Exists(SharedFileIndication.LatchPath));
        var raw = File.ReadAllText(SharedFileIndication.LatchPath);
        Assert.Contains("shared_file_latch/v1", raw, StringComparison.Ordinal);
        Assert.Contains("\"shared\": true", raw, StringComparison.OrdinalIgnoreCase);
    }
}
