#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class AdxMutateTraceHostWriteTests
{
    [Fact]
    public void RecordOutsideIde_marks_host_write_warn_once_per_episode()
    {
        var path = Path.Combine(Path.GetTempPath(), "adx-host-write-" + Guid.NewGuid().ToString("N") + ".cs");
        var mtime = DateTime.UtcNow;

        AdxMutateTrace.ClearOutsideIdeMark(path);
        AdxMutateTrace.RecordOutsideIde(path, "content", mtime);
        AdxMutateTrace.RecordOutsideIde(path, "content", mtime); // dedup

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(AdxMutateTrace.EvaluateRecent()));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("host_write").GetInt32() >= 1);
        Assert.Contains("host_write", root.GetProperty("pulse").GetString(), StringComparison.Ordinal);

        var bad = root.GetProperty("recent_bad").EnumerateArray()
            .Count(e => e.GetProperty("op").GetString() == AdxMutateTrace.OpHostWrite
                && e.GetProperty("path").GetString() == Path.GetFileName(path));
        Assert.Equal(1, bad);

        AdxMutateTrace.ClearOutsideIdeMark(path);
        AdxMutateTrace.RecordOutsideIde(path, "content", mtime.AddSeconds(1));
        using var doc2 = JsonDocument.Parse(JsonSerializer.Serialize(AdxMutateTrace.EvaluateRecent()));
        var bad2 = doc2.RootElement.GetProperty("recent_bad").EnumerateArray()
            .Count(e => e.GetProperty("op").GetString() == AdxMutateTrace.OpHostWrite
                && e.GetProperty("path").GetString() == Path.GetFileName(path));
        Assert.True(bad2 >= 2);
    }
}
