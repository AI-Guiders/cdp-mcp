using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Class-B guard: T:-only wire must REFUSE on csharp edit anchors — semantic resolver
/// silently degrades to file scope + needle text range when the type is outside the
/// loaded workspace, and place semantics change (ate '{' on CdpStateStoreTests L11, 2026-09-08).
/// </summary>
public class DocumentAnchorEditGuardTests : IDisposable
{
    readonly string _root;
    readonly SessionContext _session = new();

    public DocumentAnchorEditGuardTests()
    {
        _root = Directory.CreateTempSubdirectory("anchor-guard-").FullName;
        _session.ProjectRoot = _root;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Tonly_csharp_anchor_refuses_with_guidance()
    {
        var file = Path.Combine(_root, "Sample.cs");
        File.WriteAllText(file, "public class Sample\n{\n}\n");

        var store = new DocumentBufferStore();
        var buf = store.Open(file);

        var ex = Assert.Throws<ArgumentException>(() => DocumentAnchorEdit.Apply(
            store, _session, buf,
            new Dictionary<string, JsonElement>
            {
                ["anchor"] = JsonSerializer.SerializeToElement("[F:Sample.cs;T:Sample]"),
                ["text"] = JsonSerializer.SerializeToElement("x"),
                ["place"] = JsonSerializer.SerializeToElement("end")
            }));

        Assert.Contains("T:-only wire is not a csharp edit-anchor axis", ex.Message);
    }

    [Fact]
    public void Line_only_corridor_still_works_after_guard()
    {
        var file = Path.Combine(_root, "Sample.cs");
        File.WriteAllText(file, "public class Sample\n{\n}\n");

        var store = new DocumentBufferStore();
        var buf = store.Open(file);

        var result = DocumentAnchorEdit.Apply(
            store, _session, buf,
            new Dictionary<string, JsonElement>
            {
                ["anchor"] = JsonSerializer.SerializeToElement("[F:Sample.cs;L:1]"),
                ["text"] = JsonSerializer.SerializeToElement("// ok"),
                ["place"] = JsonSerializer.SerializeToElement("before")
            });

        Assert.Equal("line_literal", (string?)result.GetType().GetProperty("family")?.GetValue(result));
    }
}