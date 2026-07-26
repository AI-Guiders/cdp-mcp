#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeProblemsChannelTests
{
    [Fact]
    public void Build_empty_when_no_cached_diags()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var snap = IdeProblemsChannel.Build(store, session);
        Assert.True(snap.Ok);
        Assert.Equal(0, snap.Errors);
        Assert.Empty(snap.Rows);
        Assert.Contains("no diags", snap.Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_lists_cached_row_with_anchor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-problems-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Sample.cs");
        File.WriteAllText(path, "class C { void M() { int x = 1; } }\n");

        var store = new DocumentBufferStore();
        var buf = store.Open(path);
        buf.LastDiagnosticsJson = JsonSerializer.Serialize(new
        {
            data = new
            {
                items = new object[]
                {
                    new
                    {
                        severity = "error",
                        id = "CS0001",
                        message = "boom",
                        line = 1,
                        end_line = 1,
                        anchor = "[F:Sample.cs; L:1]"
                    }
                }
            }
        });
        buf.LastDiagnosedVersion = buf.Version;

        var session = new SessionContext { ProjectRoot = dir };
        var board = IdeProblemsChannel.Handle(store, session);
        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("problems", root.GetProperty("go").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("errors").GetInt32());
        Assert.Equal(1, root.GetProperty("rows").GetArrayLength());
        Assert.Equal("p1", root.GetProperty("rows")[0].GetProperty("id").GetString());
        Assert.Contains("Sample.cs", root.GetProperty("rows")[0].GetProperty("anchor").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
