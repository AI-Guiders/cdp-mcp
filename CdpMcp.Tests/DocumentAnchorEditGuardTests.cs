using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// T:=Type canon (2026-09-08): [F:;T:Type] resolves the type declaration span —
/// place=end|append inserts inside braces (body end), place=before|after stays
/// literal outside (sibling before decl / after closing brace). Needle-only
/// (Text:/Needle:/Content:) on csharp refuses — place semantics change
/// (ate '{' on CdpStateStoreTests L11, 2026-09-08).
/// Asserts pin buf.Text (edit-plane SSOT); disk persistence is the MCP
/// Instant-Save layer, not DocumentAnchorEdit.Apply.
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

    static (DocumentBufferStore Store, DocBuffer Buf) OpenSample(string root, string body)
    {
        var file = Path.Combine(root, "Sample.cs");
        File.WriteAllText(file, body);
        var store = new DocumentBufferStore();
        return (store, store.Open(file));
    }

    static Dictionary<string, JsonElement> Args(string wire, string text, string place) => new()
    {
        ["anchor"] = JsonSerializer.SerializeToElement(wire),
        ["text"] = JsonSerializer.SerializeToElement(text),
        ["place"] = JsonSerializer.SerializeToElement(place)
    };

    static string Norm(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void Type_anchor_place_end_appends_inside_body()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n    int x;\n}\n");

        var result = DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;T:Sample]", "    int y;\n", "end"));

        Assert.Equal("csharp", (string?)result.GetType().GetProperty("family")?.GetValue(result));
        Assert.Contains("    int y;\n}", Norm(buf.Text));
    }

    [Fact]
    public void Type_anchor_append_alias_maps_to_end()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n    int x;\n}\n");

        DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;T:Sample]", "    const int Z = 1;\n", "append"));

        Assert.Contains("    const int Z = 1;\n}", Norm(buf.Text));
    }

    [Fact]
    public void Type_anchor_place_after_inserts_sibling_after_close()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n}\n");

        DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;T:Sample]", "\npublic sealed class Zed\n{\n}\n", "after"));

        Assert.Contains("}\npublic sealed class Zed", Norm(buf.Text));
    }

    [Fact]
    public void Type_anchor_place_before_inserts_sibling_before_decl()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n}\n");

        DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;T:Sample]", "public interface IFoo\n{\n}\n\n", "before"));

        Assert.StartsWith("public interface IFoo", Norm(buf.Text));
    }

    [Fact]
    public void Type_anchor_missing_type_refuses_honestly()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n}\n");

        var ex = Assert.Throws<ArgumentException>(() => DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;T:Nope]", "x", "end")));

        Assert.Contains("type_not_found", ex.Message);
    }

    [Fact]
    public void Needle_only_csharp_anchor_refuses_with_guidance()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n    // Sample here\n}\n");

        var ex = Assert.Throws<ArgumentException>(() => DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;Text:Sample]", "x", "after")));

        Assert.Contains("Needle-only wire is not a csharp edit-anchor axis", ex.Message);
    }

    [Fact]
    public void Line_only_corridor_still_works()
    {
        var (store, buf) = OpenSample(_root, "public class Sample\n{\n}\n");

        var result = DocumentAnchorEdit.Apply(
            store, _session, buf,
            Args("[F:Sample.cs;L:1]", "// ok", "before"));

        Assert.Equal("line_literal", (string?)result.GetType().GetProperty("family")?.GetValue(result));
    }
}